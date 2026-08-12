using System;
using System.Collections.Generic;

using S = ServerPackets;

namespace MonoShare.MirScenes;

/// <summary>
/// Main-thread state seam for the Android item seal and rental flows.
///
/// The server owns all item, gold, expiry, and eligibility decisions.  This
/// type only records the user's current selections and the protocol state
/// needed to render the mobile window and prevent duplicate requests.
/// </summary>
public sealed class MobileSealRentalState
{
    public const long RequestTimeoutMs = 5000;
    public const int MinRentalDays = 1;
    public const int MaxRentalDays = 30;
    public const string SealErrorPrefix = "封印：";
    public const string RentalErrorPrefix = "租赁：";

    public enum ErrorDomain
    {
        None,
        Seal,
        Rental,
    }

    public sealed class RentedItemSnapshot
    {
        public ulong ItemId { get; internal set; }
        public string ItemName { get; internal set; } = string.Empty;
        public string RentingPlayerName { get; internal set; } = string.Empty;
        public DateTime ItemReturnDate { get; internal set; }
    }

    public enum SealStage
    {
        None,
        MaterialSelected,
        TargetSelected,
        Pending,
    }

    public enum RentalOperation
    {
        None,
        Request,
        Fee,
        Period,
        Deposit,
        Retrieve,
        Cancel,
        LockFee,
        LockItem,
        Confirm,
    }

    private readonly List<RentedItemSnapshot> _rentedItems = new List<RentedItemSnapshot>();
    private readonly Dictionary<ulong, DateTime> _sealedExpiryByItem = new Dictionary<ulong, DateTime>();
    private long _pendingSinceMs = -1;
    private uint _pendingFeeAmount;
    private bool _pendingUncertain;
    private int _pendingFrom = -1;
    private int _pendingTo = -1;
    private ulong _pendingItemUniqueId;
    private ulong _pendingRetrieveUniqueId;
    private UserItem _pendingItemSnapshot;
    private ulong _lateSealMaterialId;
    private ulong _lateSealTargetId;

    public bool IsOpen { get; private set; }
    public string Error { get; private set; }
    public int Revision { get; private set; }

    public SealStage CurrentSealStage { get; private set; }
    public ulong SealMaterialId { get; private set; }
    public ulong SealTargetId { get; private set; }
    public bool HasSealMaterial => SealMaterialId != 0;
    public bool HasSealTarget => SealTargetId != 0;
    public bool HasSealSelection => HasSealMaterial && HasSealTarget;
    public bool SealRequestPending => CurrentSealStage == SealStage.Pending;
    public bool? LastSealSucceeded { get; private set; }
    public IReadOnlyDictionary<ulong, DateTime> SealedExpiryByItem => _sealedExpiryByItem;

    public bool RentalSessionActive { get; private set; }
    public bool IsRenting { get; private set; }
    public string RentalPartnerName { get; private set; } = string.Empty;
    public uint RentalFee { get; private set; }
    public uint RentalDays { get; private set; }
    public UserItem RentalLoanItem { get; private set; }
    public UserItem RentalDepositedItem { get; private set; }
    public bool HasRentalLoanItem => RentalLoanItem != null;
    public bool LocalFeeLocked { get; private set; }
    public bool LocalItemLocked { get; private set; }
    public bool PartnerFeeLocked { get; private set; }
    public bool PartnerItemLocked { get; private set; }
    public bool CanConfirmRental { get; private set; }
    public bool? LastRentalConfirmed { get; private set; }
    public RentalOperation PendingRentalOperation { get; private set; }
    public bool PendingOperationUncertain => _pendingUncertain;
    public IReadOnlyList<RentedItemSnapshot> RentedItems => _rentedItems;

    public static bool IsRentableItem(UserItem item)
    {
        if (item?.Info == null)
            return false;

        if (item.RentalInformation?.RentalLocked == true)
            return false;

        if (item.Info.Bind.HasFlag(BindMode.UnableToRent))
            return false;

        return item.RentalInformation == null ||
               !item.RentalInformation.BindingFlags.HasFlag(BindMode.UnableToRent);
    }

    public static bool IsSealMaterial(UserItem item)
    {
        return item?.Info != null && item.Info.Type == ItemType.宝玉神珠 && item.Info.Shape == 8;
    }

    public static bool IsSealTarget(UserItem item)
    {
        if (item?.Info == null || (byte)item.Info.Type < 1 || (byte)item.Info.Type > 11)
            return false;

        return !item.Info.Bind.HasFlag(BindMode.DontUpgrade) &&
               item.Info.Unique == SpecialItemMode.None &&
               !(item.RentalInformation?.BindingFlags.HasFlag(BindMode.DontUpgrade) ?? false);
    }

    public static bool TryValidateRentalItemSelection(UserItem[] inventory, int slot,
        ulong expectedUniqueId, out UserItem item)
    {
        item = null;
        if (inventory == null || slot < 0 || slot >= inventory.Length || expectedUniqueId == 0)
            return false;

        item = inventory[slot];
        if (item == null || item.UniqueID != expectedUniqueId || !IsRentableItem(item))
        {
            item = null;
            return false;
        }

        return true;
    }

    public static bool CanAffordFee(uint amount, uint availableGold)
    {
        return amount != 0 && amount <= availableGold;
    }

    public static ErrorDomain ClassifyError(string message)
    {
        if (message?.StartsWith(RentalErrorPrefix, StringComparison.Ordinal) == true)
            return ErrorDomain.Rental;
        if (message?.StartsWith(SealErrorPrefix, StringComparison.Ordinal) == true)
            return ErrorDomain.Seal;
        return ErrorDomain.None;
    }

    public static string FormatError(ErrorDomain domain, string message)
    {
        string text = (message ?? string.Empty).Trim();
        string prefix = domain == ErrorDomain.Rental ? RentalErrorPrefix : SealErrorPrefix;
        if (text.StartsWith(prefix, StringComparison.Ordinal))
            return text;
        return prefix + (text.Length == 0 ? "操作失败。" : text);
    }

    public bool SelectSealMaterial(ulong uniqueId)
    {
        if (uniqueId == 0 || SealRequestPending || RentalSessionActive || PendingRentalOperation != RentalOperation.None)
            return false;

        SealMaterialId = uniqueId;
        SealTargetId = 0;
        CurrentSealStage = SealStage.MaterialSelected;
        LastSealSucceeded = null;
        Error = null;
        Touch();
        return true;
    }

    public bool SelectSealTarget(ulong uniqueId)
    {
        if (uniqueId == 0 || uniqueId == SealMaterialId || SealRequestPending ||
            RentalSessionActive || PendingRentalOperation != RentalOperation.None || !HasSealMaterial)
            return false;

        SealTargetId = uniqueId;
        CurrentSealStage = SealStage.TargetSelected;
        LastSealSucceeded = null;
        Error = null;
        Touch();
        return true;
    }

    public void ClearSealSelection()
    {
        if (SealRequestPending)
            return;

        SealMaterialId = 0;
        SealTargetId = 0;
        CurrentSealStage = SealStage.None;
        LastSealSucceeded = null;
        Touch();
    }

    public bool BeginSealRequest(long nowMs = 0)
    {
        if (!HasSealSelection || SealRequestPending || RentalSessionActive ||
            PendingRentalOperation != RentalOperation.None)
        {
            SetError(PendingRentalOperation != RentalOperation.None ? ErrorDomain.Rental : ErrorDomain.Seal,
                PendingRentalOperation != RentalOperation.None
                    ? "租赁操作正在处理中，请稍后再试。"
                    : "请先选择封印材料和目标装备。", clearRental: false);
            return false;
        }

        ClearLateSealContext();
        CurrentSealStage = SealStage.Pending;
        LastSealSucceeded = null;
        _pendingSinceMs = NormalizeClock(nowMs);
        _pendingUncertain = false;
        Error = null;
        Touch();
        return true;
    }

    public bool ApplyCombineResult(S.CombineItem packet)
    {
        if (packet == null)
        {
            SetError(ErrorDomain.Seal, "封印结果为空。", clearRental: false);
            return false;
        }

        bool lateSeal = !SealRequestPending &&
                        packet.IDFrom == _lateSealMaterialId &&
                        packet.IDTo == _lateSealTargetId &&
                        _lateSealTargetId != 0;
        if (!SealRequestPending && !lateSeal)
            return false;

        bool matches = lateSeal
            ? true
            : packet.IDFrom == SealMaterialId && packet.IDTo == SealTargetId;
        if (!matches)
            return false;

        if (lateSeal)
        {
            LastSealSucceeded = packet.Success;
            ClearLateSealContext();
            if (!packet.Success)
                Error = FormatError(ErrorDomain.Seal, "封印未完成，请查看系统提示。");
            else if (!RentalSessionActive || ClassifyError(Error) != ErrorDomain.Rental)
                Error = null;
            Touch();
            return true;
        }

        LastSealSucceeded = packet.Success;
        CurrentSealStage = packet.Success ? SealStage.None : SealStage.TargetSelected;
        if (packet.Success)
        {
            SealMaterialId = 0;
            SealTargetId = 0;
            Error = null;
        }
        else
        {
            Error = FormatError(ErrorDomain.Seal, "封印未完成，请查看系统提示。");
        }

        _pendingSinceMs = -1;
        _pendingUncertain = false;
        Touch();
        return true;
    }

    public bool ApplyItemSealChanged(S.ItemSealChanged packet)
    {
        if (packet == null || packet.UniqueID == 0)
            return false;

        _sealedExpiryByItem[packet.UniqueID] = packet.ExpiryDate;
        bool lateSeal = packet.UniqueID == _lateSealTargetId && _lateSealTargetId != 0;
        if (packet.UniqueID == SealTargetId && SealRequestPending)
        {
            CurrentSealStage = SealStage.None;
            SealMaterialId = 0;
            SealTargetId = 0;
            LastSealSucceeded = true;
            _pendingSinceMs = -1;
            _pendingUncertain = false;
        }

        if (lateSeal)
        {
            LastSealSucceeded = true;
            ClearLateSealContext();
        }

        if (!RentalSessionActive || ClassifyError(Error) != ErrorDomain.Rental)
            Error = null;
        Touch();
        return true;
    }

    public bool BeginRentalRequest(long nowMs = 0)
    {
        if (RentalSessionActive || SealRequestPending || PendingRentalOperation != RentalOperation.None)
        {
            SetError(ErrorDomain.Rental, "租赁请求已在处理中。", clearRental: false);
            return false;
        }

        BeginRentalOperation(RentalOperation.Request, nowMs);
        return true;
    }

    public bool ApplyRentalRequest(S.ItemRentalRequest packet)
    {
        if (packet == null || string.IsNullOrWhiteSpace(packet.Name))
        {
            SetError(ErrorDomain.Rental, "租赁请求无效。", clearRental: false);
            return false;
        }

        // S.ItemRentalRequest is authoritative: the server has already
        // paired both players, so an outstanding local seal request must not
        // make us silently drop this session. Keep the seal IDs privately so
        // a late CombineItem/ItemSealChanged response can still reconcile by
        // identity without blocking the rental UI.
        if (SealRequestPending)
        {
            _lateSealMaterialId = SealMaterialId;
            _lateSealTargetId = SealTargetId;
            CurrentSealStage = SealStage.None;
            SealMaterialId = 0;
            SealTargetId = 0;
            LastSealSucceeded = null;
        }

        RentalSessionActive = true;
        IsRenting = packet.Renting;
        RentalPartnerName = packet.Name.Trim();
        RentalFee = 0;
        RentalDays = 0;
        RentalLoanItem = null;
        RentalDepositedItem = null;
        _pendingFeeAmount = 0;
        LocalFeeLocked = false;
        LocalItemLocked = false;
        PartnerFeeLocked = false;
        PartnerItemLocked = false;
        CanConfirmRental = false;
        LastRentalConfirmed = null;
        PendingRentalOperation = RentalOperation.None;
        _pendingSinceMs = -1;
        _pendingUncertain = false;
        ClearPendingRentalContext();
        Error = null;
        IsOpen = true;
        Touch();
        return true;
    }

    public bool BeginRentalFee(uint amount, long nowMs = 0)
    {
        // The renter (Renting=true) chooses and pays the fee. The owner only
        // receives S.ItemRentalFee and later receives the locked amount.
        if (!RentalSessionActive || SealRequestPending || !IsRenting || LocalFeeLocked || amount == 0 ||
            PendingRentalOperation != RentalOperation.None)
            return false;

        if ((ulong)RentalFee + amount >= uint.MaxValue)
            return false;

        _pendingFeeAmount = amount;
        BeginRentalOperation(RentalOperation.Fee, nowMs);
        return true;
    }

    public bool BeginRentalFee(uint amount, uint availableGold, long nowMs)
    {
        if (!CanAffordFee(amount, availableGold))
        {
            SetError(ErrorDomain.Rental, "租金必须大于0且不超过当前金币。", clearRental: false);
            return false;
        }

        return BeginRentalFee(amount, nowMs);
    }

    public bool ApplyRentalFee(S.ItemRentalFee packet)
    {
        if (packet == null || !RentalSessionActive || IsRenting || packet.Amount == 0)
            return false;

        if ((ulong)RentalFee + packet.Amount >= uint.MaxValue)
            return false;

        RentalFee += packet.Amount;
        Error = null;
        Touch();
        return true;
    }

    public bool ApplyLocalGoldLoss(uint amount)
    {
        if (!RentalSessionActive || !IsRenting || PendingRentalOperation != RentalOperation.Fee ||
            amount == 0 || amount != _pendingFeeAmount || (ulong)RentalFee + amount >= uint.MaxValue)
            return false;

        RentalFee += amount;
        _pendingFeeAmount = 0;
        CompleteRentalOperation();
        return true;
    }

    public bool BeginRentalPeriod(uint days, long nowMs = 0)
    {
        if (!RentalSessionActive || SealRequestPending || IsRenting || LocalItemLocked ||
            days < MinRentalDays || days > MaxRentalDays || PendingRentalOperation != RentalOperation.None)
            return false;

        RentalDays = days;
        // C.ItemRentalPeriod has no acknowledgement for the owner: the
        // server forwards S.ItemRentalPeriod only to the renter. Keep the
        // local value immediately and do not invent an outstanding reply.
        PendingRentalOperation = RentalOperation.None;
        _pendingSinceMs = -1;
        Error = null;
        Touch();
        return true;
    }

    public bool ApplyRentalPeriod(S.ItemRentalPeriod packet)
    {
        if (packet == null || !RentalSessionActive || !IsRenting ||
            packet.Days < MinRentalDays || packet.Days > MaxRentalDays)
            return false;

        RentalDays = packet.Days;
        Error = null;
        if (PendingRentalOperation == RentalOperation.Period)
            CompleteRentalOperation();
        else
            Touch();
        return true;
    }

    public bool BeginDeposit(int from, int to, long nowMs = 0)
    {
        if (!CanBeginDeposit(from, to) || PendingRentalOperation != RentalOperation.None)
            return false;

        ClearPendingRentalContext();
        _pendingFrom = from;
        _pendingTo = to;
        BeginRentalOperation(RentalOperation.Deposit, nowMs);
        return true;
    }

    public bool BeginDeposit(int from, int to, UserItem[] inventory, long nowMs = 0)
    {
        if (!CanBeginDeposit(from, to) || PendingRentalOperation != RentalOperation.None ||
            inventory == null || from < 0 || from >= inventory.Length || !IsRentableItem(inventory[from]))
            return false;

        ClearPendingRentalContext();
        _pendingFrom = from;
        _pendingTo = to;
        _pendingItemSnapshot = inventory[from];
        _pendingItemUniqueId = _pendingItemSnapshot.UniqueID;
        BeginRentalOperation(RentalOperation.Deposit, nowMs);
        return true;
    }

    public void SetLocalRentalDepositedItem(UserItem item)
    {
        RentalDepositedItem = item;
        Touch();
    }

    public bool ApplyDeposit(S.DepositRentalItem packet)
    {
        if (packet == null || !RentalSessionActive || IsRenting ||
            PendingRentalOperation != RentalOperation.Deposit || !MatchesPendingSlot(packet.From, packet.To))
            return false;

        if (packet.Success && _pendingItemSnapshot != null)
            RentalDepositedItem = _pendingItemSnapshot;

        if (packet.Success)
            LocalItemLocked = false;
        CompleteRentalOperation();
        return true;
    }

    public bool ApplyDeposit(S.DepositRentalItem packet, UserItem[] inventory)
    {
        if (packet == null || !RentalSessionActive || IsRenting ||
            PendingRentalOperation != RentalOperation.Deposit || !MatchesPendingSlot(packet.From, packet.To))
            return false;

        if (!packet.Success)
        {
            CompleteRentalOperation();
            return true;
        }

        if (_pendingItemSnapshot == null || _pendingItemUniqueId == 0 || inventory == null)
            return false;

        int sourceIndex = FindInventoryItemIndex(inventory, _pendingItemUniqueId);
        if (sourceIndex < 0)
            return false;

        // The ordered packet stream may have moved the item after the request.
        // Remove the matching identity, never blindly remove the original slot.
        inventory[sourceIndex] = null;
        RentalDepositedItem = _pendingItemSnapshot;
        LocalItemLocked = false;
        CompleteRentalOperation();
        return true;
    }

    public bool BeginRetrieve(int from, int to, long nowMs = 0)
    {
        if (!CanBeginRetrieve(from, to) || PendingRentalOperation != RentalOperation.None)
            return false;

        ClearPendingRentalContext();
        _pendingFrom = from;
        _pendingTo = to;
        _pendingRetrieveUniqueId = RentalDepositedItem.UniqueID;
        BeginRentalOperation(RentalOperation.Retrieve, nowMs);
        return true;
    }

    public bool BeginRetrieve(int from, int to, UserItem[] inventory, long nowMs = 0)
    {
        if (!CanBeginRetrieve(from, to) || PendingRentalOperation != RentalOperation.None ||
            inventory == null || to < 0 || to >= inventory.Length || inventory[to] != null)
            return false;

        ClearPendingRentalContext();
        _pendingFrom = from;
        _pendingTo = to;
        _pendingRetrieveUniqueId = RentalDepositedItem.UniqueID;
        BeginRentalOperation(RentalOperation.Retrieve, nowMs);
        return true;
    }

    public bool ApplyRetrieve(S.RetrieveRentalItem packet)
    {
        if (packet == null || !RentalSessionActive || IsRenting ||
            PendingRentalOperation != RentalOperation.Retrieve || !MatchesPendingSlot(packet.From, packet.To))
            return false;

        CompleteRentalOperation();
        return true;
    }

    public bool ApplyRetrieve(S.RetrieveRentalItem packet, UserItem[] inventory)
    {
        if (packet == null || !RentalSessionActive || IsRenting)
            return false;

        if (PendingRentalOperation == RentalOperation.Retrieve)
            return ApplyNormalRetrieve(packet, inventory);

        // Server CancelItemRental first sends RetrieveRentalItem(0, j) for
        // the deposited item and then sends CancelItemRental.  A remote
        // cancel has no local pending operation, while a local close has
        // Pending=Cancel.  Accept only that exact server shape and current
        // empty slot so unrelated/late retrieve packets cannot mutate state.
        if (PendingRentalOperation != RentalOperation.Cancel &&
            PendingRentalOperation != RentalOperation.None)
            return false;

        if (inventory == null || packet.From != 0 || packet.To < 0 || packet.To >= inventory.Length ||
            RentalDepositedItem == null || inventory[packet.To] != null)
            return false;

        if (packet.Success)
        {
            inventory[packet.To] = RentalDepositedItem;
            RentalDepositedItem = null;
        }

        Touch();
        return true;
    }

    public bool ApplyRentalUpdate(S.UpdateRentalItem packet)
    {
        if (packet == null || !RentalSessionActive || !IsRenting)
            return false;

        RentalLoanItem = packet.HasData ? packet.LoanItem : null;
        Error = null;
        Touch();
        return true;
    }

    public bool BeginLockFee(long nowMs = 0)
    {
        if (!RentalSessionActive || SealRequestPending || !IsRenting || RentalFee == 0 || LocalFeeLocked ||
            PendingRentalOperation != RentalOperation.None)
            return false;

        BeginRentalOperation(RentalOperation.LockFee, nowMs);
        return true;
    }

    public bool BeginLockItem(long nowMs = 0)
    {
        if (!RentalSessionActive || SealRequestPending || IsRenting || RentalDays < MinRentalDays ||
            RentalDays > MaxRentalDays || RentalDepositedItem == null || LocalItemLocked ||
            PendingRentalOperation != RentalOperation.None)
            return false;

        BeginRentalOperation(RentalOperation.LockItem, nowMs);
        return true;
    }

    public bool ApplyRentalLock(S.ItemRentalLock packet)
    {
        if (packet == null || !RentalSessionActive)
            return false;

        if (packet.Success && ((IsRenting && !packet.GoldLocked) || (!IsRenting && !packet.ItemLocked)))
            return false;

        if (packet.Success)
        {
            LocalFeeLocked = packet.GoldLocked;
            LocalItemLocked = packet.ItemLocked;
        }

        if (PendingRentalOperation == RentalOperation.LockFee || PendingRentalOperation == RentalOperation.LockItem)
            CompleteRentalOperation();
        return true;
    }

    public bool ApplyRentalPartnerLock(S.ItemRentalPartnerLock packet)
    {
        if (packet == null || !RentalSessionActive)
            return false;

        PartnerFeeLocked = packet.GoldLocked;
        PartnerItemLocked = packet.ItemLocked;
        Error = null;
        Touch();
        return true;
    }

    public bool ApplyCanConfirmRental()
    {
        if (!RentalSessionActive)
            return false;

        CanConfirmRental = true;
        Error = null;
        Touch();
        return true;
    }

    public bool BeginConfirmRental(long nowMs = 0)
    {
        // The owner holds ItemRentalDepositedItem; the server's
        // ConfirmItemRental implementation therefore accepts the owner's
        // confirm and broadcasts S.ConfirmItemRental to both peers.
        if (!RentalSessionActive || SealRequestPending || IsRenting || !CanConfirmRental ||
            RentalDays < MinRentalDays || RentalDays > MaxRentalDays || RentalFee == 0 ||
            RentalDepositedItem == null || !LocalItemLocked || !PartnerFeeLocked ||
            PendingRentalOperation != RentalOperation.None)
            return false;

        BeginRentalOperation(RentalOperation.Confirm, nowMs);
        return true;
    }

    public bool ApplyConfirmRental()
    {
        if (!RentalSessionActive)
            return false;

        LastRentalConfirmed = true;
        ClearRentalSession();
        Touch();
        return true;
    }

    public bool BeginCancelRental(long nowMs = 0)
    {
        if (!RentalSessionActive || SealRequestPending || PendingRentalOperation == RentalOperation.Cancel)
            return false;

        _pendingFeeAmount = 0;
        ClearPendingRentalContext();
        BeginRentalOperation(RentalOperation.Cancel, nowMs);
        return true;
    }

    public bool ApplyCancelRental()
    {
        if (!RentalSessionActive && PendingRentalOperation == RentalOperation.None)
            return false;

        LastRentalConfirmed = false;
        ClearRentalSession();
        Touch();
        return true;
    }

    public bool ApplyRentedItems(S.GetRentedItems packet)
    {
        if (packet == null)
        {
            SetError(ErrorDomain.Rental, "租赁清单为空。", clearRental: false);
            return false;
        }

        _rentedItems.Clear();
        if (packet.RentedItems != null)
        {
            for (int i = 0; i < packet.RentedItems.Count; i++)
            {
                ItemRentalInformation item = packet.RentedItems[i];
                if (item == null)
                    continue;

                _rentedItems.Add(new RentedItemSnapshot
                {
                    ItemId = item.ItemId,
                    ItemName = item.ItemName ?? string.Empty,
                    RentingPlayerName = item.RentingPlayerName ?? string.Empty,
                    ItemReturnDate = item.ItemReturnDate,
                });
            }
        }

        Error = null;
        IsOpen = true;
        Touch();
        return true;
    }

    public bool ApplyServerSystemMessage(string message)
    {
        string text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;

        bool sealFailure = IsSealFailureMessage(text);
        bool rentalFailure = IsRentalFailureMessage(text);
        if (SealRequestPending && !sealFailure)
            return false;
        if (PendingRentalOperation == RentalOperation.None && !SealRequestPending)
            return false;
        if (!SealRequestPending && PendingRentalOperation != RentalOperation.None && !rentalFailure)
            return false;

        Error = FormatError(SealRequestPending ? ErrorDomain.Seal : ErrorDomain.Rental, text);
        if (SealRequestPending)
        {
            CurrentSealStage = HasSealSelection ? SealStage.TargetSelected : SealStage.None;
            _pendingSinceMs = -1;
            _pendingUncertain = false;
        }
        else if (PendingRentalOperation != RentalOperation.None)
        {
            PendingRentalOperation = RentalOperation.None;
            _pendingSinceMs = -1;
            _pendingUncertain = false;
            _pendingFeeAmount = 0;
            ClearPendingRentalContext();
        }

        IsOpen = true;
        Touch();
        return true;
    }

    public bool Tick(long nowMs)
    {
        if (_pendingSinceMs < 0)
            return false;

        long now = Math.Max(0, nowMs);
        if (now < _pendingSinceMs || now - _pendingSinceMs < RequestTimeoutMs)
            return false;

        ErrorDomain domain = SealRequestPending ? ErrorDomain.Seal : ErrorDomain.Rental;
        string operation = domain == ErrorDomain.Seal ? "封印" : "租赁";
        if (!SealRequestPending && PendingRentalOperation == RentalOperation.Request)
        {
            // A request has no server-side item/gold mutation yet, so it is
            // safe to release the UI gate and let the player retry it.
            PendingRentalOperation = RentalOperation.None;
            _pendingUncertain = false;
            _pendingSinceMs = -1;
            Error = FormatError(domain, operation + "请求超时，可重试。");
            Touch();
            return true;
        }
        // A timeout does not prove that the server rejected a non-idempotent
        // request. Keep the operation pending/uncertain so the UI cannot send
        // a duplicate fee, deposit, lock, or combine request. An authoritative
        // response, failure chat, Cancel, or session reset is required to clear
        // it.
        _pendingUncertain = true;
        Error = FormatError(domain, operation + "请求超时，等待服务器结果或取消。");
        _pendingSinceMs = -1;
        IsOpen = true;
        Touch();
        return true;
    }

    public void ResetForSession()
    {
        IsOpen = false;
        Error = null;
        CurrentSealStage = SealStage.None;
        SealMaterialId = 0;
        SealTargetId = 0;
        LastSealSucceeded = null;
        _sealedExpiryByItem.Clear();
        ClearLateSealContext();
        ClearRentalSession();
        _rentedItems.Clear();
        _pendingSinceMs = -1;
        _pendingUncertain = false;
        Revision++;
    }

    private void BeginRentalOperation(RentalOperation operation, long nowMs)
    {
        PendingRentalOperation = operation;
        _pendingSinceMs = NormalizeClock(nowMs);
        _pendingUncertain = false;
        Error = null;
        Touch();
    }

    private void CompleteRentalOperation()
    {
        PendingRentalOperation = RentalOperation.None;
        _pendingSinceMs = -1;
        _pendingUncertain = false;
        _pendingFeeAmount = 0;
        ClearPendingRentalContext();
        Error = null;
        Touch();
    }

    private void ClearRentalSession()
    {
        RentalSessionActive = false;
        IsRenting = false;
        RentalPartnerName = string.Empty;
        RentalFee = 0;
        RentalDays = 0;
        RentalLoanItem = null;
        RentalDepositedItem = null;
        _pendingFeeAmount = 0;
        LocalFeeLocked = false;
        LocalItemLocked = false;
        PartnerFeeLocked = false;
        PartnerItemLocked = false;
        CanConfirmRental = false;
        PendingRentalOperation = RentalOperation.None;
        _pendingSinceMs = -1;
        _pendingUncertain = false;
        ClearPendingRentalContext();
    }

    private void SetError(ErrorDomain domain, string message, bool clearRental)
    {
        Error = FormatError(domain, string.IsNullOrWhiteSpace(message) ? "物品操作无效。" : message);
        if (clearRental)
            ClearRentalSession();
        IsOpen = true;
        Touch();
    }

    private void Touch()
    {
        Revision++;
        IsOpen = true;
    }

    private static long NormalizeClock(long nowMs) => nowMs < 0 ? 0 : nowMs;

    private bool CanBeginDeposit(int from, int to)
    {
        return RentalSessionActive && !SealRequestPending && !IsRenting && !LocalItemLocked &&
               RentalDepositedItem == null && from >= 0 && to >= 0;
    }

    private bool CanBeginRetrieve(int from, int to)
    {
        return RentalSessionActive && !SealRequestPending && !IsRenting && !LocalItemLocked &&
               RentalDepositedItem != null &&
               from >= 0 && to >= 0;
    }

    private bool MatchesPendingSlot(int from, int to)
    {
        return from == _pendingFrom && to == _pendingTo;
    }

    private bool ApplyNormalRetrieve(S.RetrieveRentalItem packet, UserItem[] inventory)
    {
        if (!MatchesPendingSlot(packet.From, packet.To))
            return false;

        if (!packet.Success)
        {
            CompleteRentalOperation();
            return true;
        }

        if (inventory == null || _pendingRetrieveUniqueId == 0 || RentalDepositedItem == null ||
            RentalDepositedItem.UniqueID != _pendingRetrieveUniqueId ||
            packet.To < 0 || packet.To >= inventory.Length || inventory[packet.To] != null)
            return false;

        inventory[packet.To] = RentalDepositedItem;
        RentalDepositedItem = null;
        CompleteRentalOperation();
        return true;
    }

    private static int FindInventoryItemIndex(UserItem[] inventory, ulong uniqueId)
    {
        if (inventory == null || uniqueId == 0)
            return -1;

        for (int i = 0; i < inventory.Length; i++)
        {
            if (inventory[i]?.UniqueID == uniqueId)
                return i;
        }

        return -1;
    }

    private void ClearPendingRentalContext()
    {
        _pendingFrom = -1;
        _pendingTo = -1;
        _pendingItemUniqueId = 0;
        _pendingRetrieveUniqueId = 0;
        _pendingItemSnapshot = null;
    }

    private void ClearLateSealContext()
    {
        _lateSealMaterialId = 0;
        _lateSealTargetId = 0;
    }

    private static bool IsSealFailureMessage(string text)
    {
        bool domain = text.IndexOf("封印", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      text.IndexOf("宝玉神珠", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      text.IndexOf("神珠", StringComparison.OrdinalIgnoreCase) >= 0;
        return domain && IsFailureMarker(text);
    }

    private static bool IsRentalFailureMessage(string text)
    {
        bool domain = text.IndexOf("租赁", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      text.IndexOf("租金", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      text.IndexOf("出租", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      text.IndexOf("租借", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      text.IndexOf("租入", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      text.IndexOf("租用", StringComparison.OrdinalIgnoreCase) >= 0;
        return domain && (IsFailureMarker(text) || IsRentalFailurePhrase(text));
    }

    private static bool IsRentalFailurePhrase(string text)
    {
        return text.IndexOf("已经将物品出租给", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("面向你想租借物品的玩家", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("面对你想租借物品的玩家", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("一次不能租用", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsFailureMarker(string text)
    {
        return text.IndexOf("无法", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("失败", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("不能", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("不足", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("拒绝", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("不在范围", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("当前正忙", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("不成功", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("取消", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
