using System;
using System.Collections.Generic;

using MonoShare.MirScenes;
using Xunit;

namespace Base05.Tests;

public sealed class MobileSealRentalStateTests
{
    [Fact]
    public void Seal_rental_usage_probe_smoke_projects_protocol_results_to_ui_values()
    {
        var state = new MobileSealRentalState();

        var sealRequest = new ClientPackets.CombineItem
        {
            Grid = MirGridType.Inventory,
            IDFrom = 11,
            IDTo = 22,
        };
        Assert.Equal((short)ClientPacketIds.CombineItem, sealRequest.Index);
        Assert.True(state.SelectSealMaterial(sealRequest.IDFrom));
        Assert.True(state.SelectSealTarget(sealRequest.IDTo));
        Assert.True(state.BeginSealRequest(10_000));
        Assert.True(state.SealRequestPending);

        var sealResult = new ServerPackets.CombineItem
        {
            IDFrom = 11,
            IDTo = 22,
            Success = true,
            Destroy = true,
        };
        Assert.Equal((short)ServerPacketIds.CombineItem, sealResult.Index);
        Assert.True(state.ApplyCombineResult(sealResult));
        Assert.True(state.LastSealSucceeded);
        Assert.False(state.HasSealSelection);

        var rentalRequest = new ClientPackets.ItemRentalRequest();
        Assert.Equal((short)ClientPacketIds.ItemRentalRequest, rentalRequest.Index);
        Assert.True(state.BeginRentalRequest(20_000));
        Assert.Equal(MobileSealRentalState.RentalOperation.Request, state.PendingRentalOperation);
        var rentalResponse = new ServerPackets.ItemRentalRequest
        {
            Name = "探针租客",
            Renting = false,
        };
        Assert.Equal((short)ServerPacketIds.ItemRentalRequest, rentalResponse.Index);
        Assert.True(state.ApplyRentalRequest(rentalResponse));

        // FairyGuiHost.MobileSealRental 刷新窗口时读取这些公开投影。
        Assert.True(state.IsOpen);
        Assert.True(state.RentalSessionActive);
        Assert.False(state.IsRenting);
        Assert.Equal("探针租客", state.RentalPartnerName);
        Assert.Equal(MobileSealRentalState.RentalOperation.None, state.PendingRentalOperation);
        Assert.Null(state.Error);
    }

    [Fact]
    public void Seal_and_rental_commands_keep_existing_packet_indexes_and_shapes()
    {
        Assert.Equal((short)ClientPacketIds.CombineItem, new ClientPackets.CombineItem
        {
            Grid = MirGridType.Inventory,
            IDFrom = 11,
            IDTo = 22,
        }.Index);
        Assert.Equal((short)ClientPacketIds.GetRentedItems, new ClientPackets.GetRentedItems().Index);
        Assert.Equal((short)ClientPacketIds.ItemRentalRequest, new ClientPackets.ItemRentalRequest().Index);
        Assert.Equal((short)ClientPacketIds.ItemRentalFee, new ClientPackets.ItemRentalFee { Amount = 100 }.Index);
        Assert.Equal((short)ClientPacketIds.ItemRentalPeriod, new ClientPackets.ItemRentalPeriod { Days = 7 }.Index);
        Assert.Equal((short)ClientPacketIds.DepositRentalItem, new ClientPackets.DepositRentalItem { From = 2, To = 0 }.Index);
        Assert.Equal((short)ClientPacketIds.RetrieveRentalItem, new ClientPackets.RetrieveRentalItem { From = 0, To = 3 }.Index);
        Assert.Equal((short)ClientPacketIds.ItemRentalLockFee, new ClientPackets.ItemRentalLockFee().Index);
        Assert.Equal((short)ClientPacketIds.ItemRentalLockItem, new ClientPackets.ItemRentalLockItem().Index);
        Assert.Equal((short)ClientPacketIds.ConfirmItemRental, new ClientPackets.ConfirmItemRental().Index);
        Assert.Equal((short)ClientPacketIds.CancelItemRental, new ClientPackets.CancelItemRental().Index);

        Assert.Equal((short)ServerPacketIds.ItemSealChanged, new ServerPackets.ItemSealChanged
        {
            UniqueID = 22,
            ExpiryDate = DateTime.UtcNow,
        }.Index);
        Assert.Equal((short)ServerPacketIds.ItemRentalLock, new ServerPackets.ItemRentalLock().Index);
        Assert.Equal((short)ServerPacketIds.ItemRentalPartnerLock, new ServerPackets.ItemRentalPartnerLock().Index);
        Assert.Equal((short)ServerPacketIds.CanConfirmItemRental, new ServerPackets.CanConfirmItemRental().Index);
        Assert.Equal((short)ServerPacketIds.ConfirmItemRental, new ServerPackets.ConfirmItemRental().Index);
    }

    [Fact]
    public void Seal_requires_material_then_target_and_only_authoritative_success_clears_selection()
    {
        var state = new MobileSealRentalState();

        Assert.False(state.BeginSealRequest(100));
        Assert.True(state.SelectSealMaterial(11));
        Assert.Equal(MobileSealRentalState.SealStage.MaterialSelected, state.CurrentSealStage);
        Assert.False(state.SelectSealTarget(11));
        Assert.True(state.SelectSealTarget(22));
        Assert.True(state.BeginSealRequest(100));
        Assert.False(state.BeginSealRequest(101));

        Assert.False(state.ApplyCombineResult(new ServerPackets.CombineItem
        {
            IDFrom = 99,
            IDTo = 22,
            Success = true,
        }));
        Assert.True(state.SealRequestPending);

        Assert.True(state.ApplyCombineResult(new ServerPackets.CombineItem
        {
            IDFrom = 11,
            IDTo = 22,
            Success = true,
            Destroy = true,
        }));
        Assert.False(state.SealRequestPending);
        Assert.Equal(MobileSealRentalState.SealStage.None, state.CurrentSealStage);
        Assert.True(state.LastSealSucceeded);
        Assert.False(state.HasSealSelection);
    }

    [Fact]
    public void Seal_failure_and_timeout_keep_retryable_selection()
    {
        var state = new MobileSealRentalState();
        state.SelectSealMaterial(11);
        state.SelectSealTarget(22);

        Assert.True(state.BeginSealRequest(1_000));
        Assert.True(state.ApplyCombineResult(new ServerPackets.CombineItem
        {
            IDFrom = 11,
            IDTo = 22,
            Success = false,
        }));
        Assert.Equal(MobileSealRentalState.SealStage.TargetSelected, state.CurrentSealStage);
        Assert.False(state.LastSealSucceeded);
        Assert.True(state.HasSealSelection);

        Assert.True(state.BeginSealRequest(1_500));
        Assert.True(state.ApplyServerSystemMessage("封印材料不足"));
        Assert.False(state.SealRequestPending);
        Assert.Contains("材料不足", state.Error);

        Assert.True(state.BeginSealRequest(2_000));
        Assert.True(state.Tick(2_000 + MobileSealRentalState.RequestTimeoutMs));
        Assert.True(state.SealRequestPending);
        Assert.True(state.PendingOperationUncertain);
        Assert.True(state.HasSealSelection);
        Assert.Contains("超时", state.Error);
        Assert.False(state.BeginSealRequest(8_000));
        Assert.True(state.ApplyServerSystemMessage("封印材料不足"));
        Assert.False(state.SealRequestPending);
    }

    [Fact]
    public void Inbound_rental_request_wins_over_seal_pending_and_late_seal_results_do_not_break_rental()
    {
        var expiry = DateTime.UtcNow.AddMinutes(5);
        var state = new MobileSealRentalState();
        Assert.True(state.SelectSealMaterial(11));
        Assert.True(state.SelectSealTarget(22));
        Assert.True(state.BeginSealRequest(100));

        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest
        {
            Name = "租客",
            Renting = false,
        }));
        Assert.True(state.RentalSessionActive);
        Assert.False(state.SealRequestPending);
        Assert.False(state.HasSealSelection);
        Assert.Equal(MobileSealRentalState.RentalOperation.None, state.PendingRentalOperation);

        Assert.True(state.ApplyItemSealChanged(new ServerPackets.ItemSealChanged
        {
            UniqueID = 22,
            ExpiryDate = expiry,
        }));
        Assert.Equal(expiry, state.SealedExpiryByItem[22]);
        Assert.True(state.LastSealSucceeded);
        Assert.True(state.RentalSessionActive);

        // Authoritative rental packets are not blocked by the old seal gate.
        Assert.True(state.ApplyRentalPartnerLock(new ServerPackets.ItemRentalPartnerLock
        {
            GoldLocked = true,
        }));
        Assert.True(state.ApplyCanConfirmRental());
        Assert.True(state.ApplyCancelRental());
        Assert.False(state.RentalSessionActive);

        var failedSeal = new MobileSealRentalState();
        Assert.True(failedSeal.SelectSealMaterial(31));
        Assert.True(failedSeal.SelectSealTarget(32));
        Assert.True(failedSeal.BeginSealRequest(200));
        Assert.True(failedSeal.ApplyRentalRequest(new ServerPackets.ItemRentalRequest
        {
            Name = "租客",
            Renting = true,
        }));
        Assert.True(failedSeal.ApplyCombineResult(new ServerPackets.CombineItem
        {
            IDFrom = 31,
            IDTo = 32,
            Success = false,
        }));
        Assert.False(failedSeal.LastSealSucceeded);
        Assert.True(failedSeal.RentalSessionActive);
        Assert.Equal(MobileSealRentalState.RentalOperation.None, failedSeal.PendingRentalOperation);
    }

    [Fact]
    public void Owner_flow_tracks_received_incremental_fee_period_deposit_locks_and_confirm()
    {
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest
        {
            Name = "租客",
            Renting = false,
        }));

        Assert.False(state.BeginRentalFee(100, 10));
        Assert.True(state.ApplyRentalFee(new ServerPackets.ItemRentalFee { Amount = 100 }));
        Assert.True(state.ApplyRentalFee(new ServerPackets.ItemRentalFee { Amount = 50 }));
        Assert.Equal((uint)150, state.RentalFee);

        // The owner has no S.ItemRentalPeriod acknowledgement; the value is
        // committed locally while the server forwards it to the renter.
        Assert.True(state.BeginRentalPeriod(7, 30));
        Assert.Equal((uint)7, state.RentalDays);
        Assert.Equal(MobileSealRentalState.RentalOperation.None, state.PendingRentalOperation);

        Assert.True(state.BeginDeposit(2, 0, 40));
        state.SetLocalRentalDepositedItem(new UserItem(new ItemInfo()));
        Assert.True(state.ApplyDeposit(new ServerPackets.DepositRentalItem
        {
            From = 2,
            To = 0,
            Success = true,
        }));
        Assert.True(state.BeginLockItem(50));
        Assert.True(state.ApplyRentalLock(new ServerPackets.ItemRentalLock
        {
            Success = true,
            ItemLocked = true,
        }));
        Assert.True(state.LocalItemLocked);
        Assert.True(state.ApplyRentalPartnerLock(new ServerPackets.ItemRentalPartnerLock
        {
            GoldLocked = true,
        }));
        Assert.True(state.PartnerFeeLocked);
        Assert.True(state.ApplyCanConfirmRental());
        Assert.True(state.BeginConfirmRental(60));
        Assert.True(state.ApplyConfirmRental());
        Assert.False(state.RentalSessionActive);
        Assert.True(state.LastRentalConfirmed);
    }

    [Fact]
    public void Renter_pays_fee_receives_period_then_locks_fee_and_waits_for_confirm()
    {
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest
        {
            Name = "出租方",
            Renting = true,
        }));

        Assert.True(state.BeginRentalFee(100, 10));
        Assert.Equal((uint)0, state.RentalFee);
        Assert.True(state.ApplyLocalGoldLoss(100));
        Assert.True(state.BeginRentalFee(50, 20));
        Assert.Equal((uint)100, state.RentalFee);
        Assert.True(state.ApplyLocalGoldLoss(50));
        Assert.Equal((uint)150, state.RentalFee);
        Assert.True(state.ApplyRentalPeriod(new ServerPackets.ItemRentalPeriod { Days = 7 }));
        Assert.Equal((uint)150, state.RentalFee);
        Assert.Equal((uint)7, state.RentalDays);

        Assert.True(state.BeginLockFee(20));
        Assert.True(state.ApplyRentalLock(new ServerPackets.ItemRentalLock
        {
            Success = true,
            GoldLocked = true,
        }));
        Assert.True(state.LocalFeeLocked);
        Assert.True(state.ApplyRentalPartnerLock(new ServerPackets.ItemRentalPartnerLock
        {
            ItemLocked = true,
        }));
        Assert.True(state.PartnerItemLocked);
        Assert.True(state.ApplyCanConfirmRental());
        Assert.False(state.BeginConfirmRental(30));
        Assert.True(state.RentalSessionActive);
        Assert.True(state.ApplyConfirmRental());
        Assert.False(state.RentalSessionActive);
    }

    [Fact]
    public void Renter_fee_checks_available_gold_before_entering_pending_state()
    {
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest
        {
            Name = "出租方",
            Renting = true,
        }));

        Assert.False(MobileSealRentalState.CanAffordFee(100, 99));
        Assert.False(state.BeginRentalFee(100, 99, 10));
        Assert.Equal((uint)0, state.RentalFee);
        Assert.Equal(MobileSealRentalState.RentalOperation.None, state.PendingRentalOperation);
        Assert.Contains("不超过当前金币", state.Error);

        Assert.True(MobileSealRentalState.CanAffordFee(100, 100));
        Assert.True(state.BeginRentalFee(100, 100, 20));
        Assert.Equal((uint)0, state.RentalFee);
        Assert.Equal(MobileSealRentalState.RentalOperation.Fee, state.PendingRentalOperation);
    }

    [Fact]
    public void Request_roles_route_each_authoritative_packet_to_the_correct_peer()
    {
        var owner = new MobileSealRentalState();
        var renter = new MobileSealRentalState();

        // Server sends Renting=false to the initiator/owner and Renting=true
        // to the player who will receive the loan item.
        Assert.True(owner.ApplyRentalRequest(new ServerPackets.ItemRentalRequest
        {
            Name = "承租人",
            Renting = false,
        }));
        Assert.True(renter.ApplyRentalRequest(new ServerPackets.ItemRentalRequest
        {
            Name = "物主",
            Renting = true,
        }));

        Assert.True(owner.BeginDeposit(3, 0, 10));
        owner.SetLocalRentalDepositedItem(new UserItem(new ItemInfo()));
        Assert.True(owner.ApplyDeposit(new ServerPackets.DepositRentalItem
        {
            From = 3,
            To = 0,
            Success = true,
        }));
        Assert.True(owner.BeginRentalPeriod(7, 20));
        Assert.False(owner.ApplyRentalPeriod(new ServerPackets.ItemRentalPeriod { Days = 7 }));
        Assert.True(renter.ApplyRentalPeriod(new ServerPackets.ItemRentalPeriod { Days = 7 }));

        Assert.True(renter.BeginRentalFee(100, 30));
        Assert.True(renter.ApplyLocalGoldLoss(100));
        Assert.True(owner.ApplyRentalFee(new ServerPackets.ItemRentalFee { Amount = 100 }));
        Assert.Equal((uint)100, owner.RentalFee);
        Assert.Equal((uint)100, renter.RentalFee);

        var loan = new UserItem(new ItemInfo());
        Assert.True(renter.ApplyRentalUpdate(new ServerPackets.UpdateRentalItem
        {
            HasData = true,
            LoanItem = loan,
        }));
        Assert.True(renter.HasRentalLoanItem);

        Assert.True(owner.BeginLockItem(40));
        Assert.True(owner.ApplyRentalLock(new ServerPackets.ItemRentalLock
        {
            Success = true,
            ItemLocked = true,
        }));
        Assert.True(renter.ApplyRentalPartnerLock(new ServerPackets.ItemRentalPartnerLock
        {
            ItemLocked = true,
        }));

        Assert.True(renter.BeginLockFee(50));
        Assert.True(renter.ApplyRentalLock(new ServerPackets.ItemRentalLock
        {
            Success = true,
            GoldLocked = true,
        }));
        Assert.True(owner.ApplyRentalPartnerLock(new ServerPackets.ItemRentalPartnerLock
        {
            GoldLocked = true,
        }));

        Assert.True(owner.ApplyCanConfirmRental());
        Assert.True(renter.ApplyCanConfirmRental());
        Assert.True(owner.BeginConfirmRental(60));
        Assert.False(renter.BeginConfirmRental(60));
        Assert.True(owner.ApplyConfirmRental());
        Assert.True(renter.ApplyConfirmRental());
        Assert.False(owner.RentalSessionActive);
        Assert.False(renter.RentalSessionActive);
    }

    [Fact]
    public void Rented_summary_and_cancel_are_authoritative_state_updates()
    {
        var state = new MobileSealRentalState();
        DateTime returnDate = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var packet = new ServerPackets.GetRentedItems
        {
            RentedItems = new List<ItemRentalInformation>
            {
                new ItemRentalInformation
                {
                    ItemId = 42,
                    ItemName = "裁决",
                    RentingPlayerName = "租客",
                    ItemReturnDate = returnDate,
                },
            },
        };

        Assert.True(state.ApplyRentedItems(packet));
        Assert.Single(state.RentedItems);
        Assert.Equal((ulong)42, state.RentedItems[0].ItemId);
        Assert.Equal(returnDate, state.RentedItems[0].ItemReturnDate);

        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest
        {
            Name = "租客",
            Renting = false,
        }));
        Assert.True(state.BeginCancelRental(100));
        Assert.True(state.ApplyCancelRental());
        Assert.False(state.RentalSessionActive);
        Assert.False(state.LastRentalConfirmed);
    }

    [Fact]
    public void Silent_rental_reply_times_out_without_inventing_a_packet()
    {
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest
        {
            Name = "租客",
            Renting = false,
        }));
        Assert.True(state.BeginDeposit(2, 0, 1_000));
        Assert.True(state.Tick(1_000 + MobileSealRentalState.RequestTimeoutMs));
        Assert.Equal(MobileSealRentalState.RentalOperation.Deposit, state.PendingRentalOperation);
        Assert.True(state.PendingOperationUncertain);
        Assert.Contains("超时", state.Error);
        Assert.False(state.BeginDeposit(2, 0, 7_000));
        Assert.True(state.ApplyServerSystemMessage("租赁物品押入失败"));
        Assert.True(state.BeginDeposit(2, 0, 7_000));
    }

    [Fact]
    public void Fee_is_committed_only_after_matching_gold_loss_and_duplicate_loss_is_ignored()
    {
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "出租方", Renting = true }));
        Assert.True(state.BeginRentalFee(25, 10));
        Assert.Equal((uint)0, state.RentalFee);
        Assert.False(state.ApplyLocalGoldLoss(24));
        Assert.True(state.ApplyLocalGoldLoss(25));
        Assert.Equal((uint)25, state.RentalFee);
        Assert.False(state.ApplyLocalGoldLoss(25));
    }

    [Fact]
    public void Seal_and_rental_requests_are_mutually_exclusive()
    {
        var seal = new MobileSealRentalState();
        Assert.True(seal.SelectSealMaterial(1));
        Assert.True(seal.SelectSealTarget(2));
        Assert.True(seal.BeginSealRequest(10));
        Assert.False(seal.BeginRentalRequest(11));

        var rental = new MobileSealRentalState();
        Assert.True(rental.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "租客", Renting = false }));
        Assert.False(rental.SelectSealMaterial(1));
        Assert.False(rental.BeginSealRequest(12));
    }

    [Fact]
    public void Rental_request_timeout_releases_only_the_pre_session_request_gate()
    {
        var state = new MobileSealRentalState();
        Assert.True(state.BeginRentalRequest(100));
        Assert.True(state.Tick(100 + MobileSealRentalState.RequestTimeoutMs));
        Assert.Equal(MobileSealRentalState.RentalOperation.None, state.PendingRentalOperation);
        Assert.False(state.PendingOperationUncertain);
        Assert.True(state.BeginRentalRequest(7_000));
    }

    [Fact]
    public void Confirm_and_item_lock_require_complete_owner_prerequisites()
    {
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "租客", Renting = false }));
        Assert.False(state.BeginConfirmRental(1));
        Assert.False(state.BeginLockItem(2));

        var item = new UserItem(new ItemInfo()) { UniqueID = 9001 };
        Assert.True(state.BeginDeposit(3, 0, 3));
        state.SetLocalRentalDepositedItem(item);
        Assert.True(state.ApplyDeposit(new ServerPackets.DepositRentalItem { From = 3, To = 0, Success = true }));
        Assert.False(state.BeginLockItem(4));
        Assert.True(state.BeginRentalPeriod(1, 5));
        Assert.True(state.BeginLockItem(6));
        Assert.True(state.ApplyRentalLock(new ServerPackets.ItemRentalLock { Success = true, ItemLocked = true }));
        Assert.False(state.BeginConfirmRental(7));
        Assert.True(state.ApplyRentalFee(new ServerPackets.ItemRentalFee { Amount = 10 }));
        Assert.True(state.ApplyRentalPartnerLock(new ServerPackets.ItemRentalPartnerLock { GoldLocked = true }));
        Assert.True(state.ApplyCanConfirmRental());
        Assert.True(state.BeginConfirmRental(8));
    }

    [Fact]
    public void Rental_selection_filters_server_rules_and_reconciles_moved_slots()
    {
        var locked = new UserItem(new ItemInfo())
        {
            UniqueID = 100,
            RentalInformation = new RentalInformation { RentalLocked = true },
        };
        var valid = new UserItem(new ItemInfo()) { UniqueID = 200 };
        var inventory = new UserItem[] { locked, valid, null, null };

        Assert.False(MobileSealRentalState.TryValidateRentalItemSelection(inventory, 0, 100, out _));
        Assert.True(MobileSealRentalState.TryValidateRentalItemSelection(inventory, 1, 200, out UserItem selected));
        Assert.Same(valid, selected);

        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "租客", Renting = false }));
        Assert.True(state.BeginDeposit(1, 0, inventory, 10));
        inventory[1] = null;
        inventory[3] = valid;
        Assert.True(state.ApplyDeposit(new ServerPackets.DepositRentalItem { From = 1, To = 0, Success = true }, inventory));
        Assert.Null(inventory[3]);
        Assert.Same(valid, state.RentalDepositedItem);

        Assert.True(state.BeginRetrieve(0, 2, inventory, 20));
        Assert.False(state.ApplyRetrieve(new ServerPackets.RetrieveRentalItem { From = 99, To = 2, Success = true }, inventory));
        Assert.Null(inventory[2]);
        Assert.True(state.ApplyRetrieve(new ServerPackets.RetrieveRentalItem { From = 0, To = 2, Success = true }, inventory));
        Assert.Same(valid, inventory[2]);
        Assert.Null(state.RentalDepositedItem);
    }

    [Fact]
    public void Owner_cannot_retrieve_locked_item_but_cancel_retrieve_is_still_applied()
    {
        var item = new UserItem(new ItemInfo()) { UniqueID = 350 };
        var inventory = new UserItem[] { item, null, null };
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest
        {
            Name = "租客",
            Renting = false,
        }));
        Assert.True(state.BeginDeposit(0, 0, inventory, 1));
        Assert.True(state.ApplyDeposit(new ServerPackets.DepositRentalItem
        {
            From = 0,
            To = 0,
            Success = true,
        }, inventory));
        Assert.True(state.BeginRentalPeriod(1, 2));
        Assert.True(state.BeginLockItem(3));
        Assert.True(state.ApplyRentalLock(new ServerPackets.ItemRentalLock
        {
            Success = true,
            ItemLocked = true,
        }));
        Assert.True(state.LocalItemLocked);

        Assert.False(state.BeginRetrieve(0, 1, inventory, 4));
        Assert.Equal(MobileSealRentalState.RentalOperation.None, state.PendingRentalOperation);
        Assert.Same(item, state.RentalDepositedItem);
        Assert.Null(inventory[1]);

        Assert.True(state.BeginCancelRental(5));
        Assert.True(state.ApplyRetrieve(new ServerPackets.RetrieveRentalItem
        {
            From = 0,
            To = 1,
            Success = true,
        }, inventory));
        Assert.Same(item, inventory[1]);
        Assert.Null(state.RentalDepositedItem);
        Assert.True(state.ApplyCancelRental());
        Assert.False(state.RentalSessionActive);
    }

    [Fact]
    public void Failed_deposit_and_retrieve_responses_clear_only_the_matching_pending_operation()
    {
        var item = new UserItem(new ItemInfo()) { UniqueID = 300 };
        var inventory = new UserItem[] { item, null, null };
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "租客", Renting = false }));
        Assert.True(state.BeginDeposit(0, 0, inventory, 1));
        Assert.True(state.ApplyDeposit(new ServerPackets.DepositRentalItem { From = 0, To = 0, Success = false }, inventory));
        Assert.Same(item, inventory[0]);
        Assert.Null(state.RentalDepositedItem);

        state.SetLocalRentalDepositedItem(item);
        Assert.True(state.BeginRetrieve(0, 1, inventory, 2));
        Assert.True(state.ApplyRetrieve(new ServerPackets.RetrieveRentalItem { From = 0, To = 1, Success = false }, inventory));
        Assert.Same(item, state.RentalDepositedItem);
        Assert.Null(inventory[1]);
    }

    [Fact]
    public void User_cancel_accepts_server_retrieve_then_cancel_without_losing_deposited_item()
    {
        var item = new UserItem(new ItemInfo()) { UniqueID = 401 };
        var inventory = new UserItem[] { item, null, null };
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "租客", Renting = false }));
        Assert.True(state.BeginDeposit(0, 0, inventory, 1));
        Assert.True(state.ApplyDeposit(new ServerPackets.DepositRentalItem { From = 0, To = 0, Success = true }, inventory));
        Assert.True(state.BeginCancelRental(2));

        Assert.True(state.ApplyRetrieve(new ServerPackets.RetrieveRentalItem { From = 0, To = 1, Success = true }, inventory));
        Assert.Same(item, inventory[1]);
        Assert.Null(state.RentalDepositedItem);
        Assert.True(state.ApplyCancelRental());
        Assert.False(state.RentalSessionActive);
        Assert.Same(item, inventory[1]);
        Assert.False(state.ApplyRetrieve(new ServerPackets.RetrieveRentalItem { From = 0, To = 2, Success = true }, inventory));
        Assert.Null(inventory[2]);
    }

    [Fact]
    public void Remote_cancel_accepts_server_retrieve_while_no_local_retrieve_is_pending()
    {
        var item = new UserItem(new ItemInfo()) { UniqueID = 402 };
        var inventory = new UserItem[] { item, null, null };
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "租客", Renting = false }));
        Assert.True(state.BeginDeposit(0, 0, inventory, 1));
        Assert.True(state.ApplyDeposit(new ServerPackets.DepositRentalItem { From = 0, To = 0, Success = true }, inventory));
        Assert.False(state.ApplyRetrieve(new ServerPackets.RetrieveRentalItem { From = 1, To = 1, Success = true }, inventory));
        Assert.True(state.ApplyRetrieve(new ServerPackets.RetrieveRentalItem { From = 0, To = 1, Success = true }, inventory));
        Assert.Same(item, inventory[1]);
        Assert.True(state.ApplyCancelRental());
        Assert.False(state.RentalSessionActive);
        Assert.False(state.ApplyRetrieve(new ServerPackets.RetrieveRentalItem { From = 0, To = 2, Success = true }, inventory));
    }

    [Fact]
    public void Successful_lock_chat_does_not_clear_fee_or_deposit_pending_state()
    {
        var renter = new MobileSealRentalState();
        Assert.True(renter.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "物主", Renting = true }));
        Assert.True(renter.BeginRentalFee(100, 1));
        Assert.False(renter.ApplyServerSystemMessage("物主已经锁定了租金"));
        Assert.Equal(MobileSealRentalState.RentalOperation.Fee, renter.PendingRentalOperation);
        Assert.True(renter.ApplyLocalGoldLoss(100));
        Assert.Equal((uint)100, renter.RentalFee);

        var owner = new MobileSealRentalState();
        Assert.True(owner.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "租客", Renting = false }));
        Assert.True(owner.BeginDeposit(0, 0, 2));
        Assert.False(owner.ApplyServerSystemMessage("租客已锁定租赁物品"));
        Assert.Equal(MobileSealRentalState.RentalOperation.Deposit, owner.PendingRentalOperation);
    }

    [Fact]
    public void Unrelated_system_chat_does_not_clear_a_pending_rental_operation()
    {
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "租客", Renting = false }));
        Assert.True(state.BeginDeposit(2, 0, 1));
        Assert.False(state.ApplyServerSystemMessage("获得金币 10"));
        Assert.Equal(MobileSealRentalState.RentalOperation.Deposit, state.PendingRentalOperation);
        Assert.True(state.ApplyServerSystemMessage("租赁物品押入失败"));
        Assert.Equal(MobileSealRentalState.RentalOperation.None, state.PendingRentalOperation);
    }

    [Fact]
    public void Error_domains_are_prefixed_and_classified_for_mobile_status_rendering()
    {
        Assert.Equal(MobileSealRentalState.ErrorDomain.Rental,
            MobileSealRentalState.ClassifyError("租赁：请求超时，可重试。"));
        Assert.Equal(MobileSealRentalState.ErrorDomain.Seal,
            MobileSealRentalState.ClassifyError("封印：结果为空。"));
        Assert.Equal(MobileSealRentalState.ErrorDomain.None,
            MobileSealRentalState.ClassifyError("请求超时，可重试。"));
        Assert.Equal("租赁：请求失败。",
            MobileSealRentalState.FormatError(MobileSealRentalState.ErrorDomain.Rental, "请求失败。"));
        Assert.Equal("封印：结果为空。",
            MobileSealRentalState.FormatError(MobileSealRentalState.ErrorDomain.Seal, "封印：结果为空。"));

        var rental = new MobileSealRentalState();
        Assert.True(rental.BeginRentalRequest(1));
        Assert.False(rental.BeginRentalRequest(2));
        Assert.Equal(MobileSealRentalState.ErrorDomain.Rental,
            MobileSealRentalState.ClassifyError(rental.Error));

        var seal = new MobileSealRentalState();
        Assert.False(seal.BeginSealRequest(1));
        Assert.Equal(MobileSealRentalState.ErrorDomain.Seal,
            MobileSealRentalState.ClassifyError(seal.Error));
    }

    [Theory]
    [InlineData("死亡时无法租用物品")]
    [InlineData("已经将物品出租给其他玩家")]
    [InlineData("面向你想租借物品的玩家")]
    [InlineData("面对你想租借物品的玩家")]
    [InlineData("一次不能租用超过3件物品")]
    public void Server_rental_request_failure_chats_clear_pending_and_keep_failure_domain(string message)
    {
        var state = new MobileSealRentalState();
        Assert.True(state.BeginRentalRequest(1));
        Assert.True(state.ApplyServerSystemMessage(message));
        Assert.Equal(MobileSealRentalState.RentalOperation.None, state.PendingRentalOperation);
        Assert.Equal(MobileSealRentalState.ErrorDomain.Rental,
            MobileSealRentalState.ClassifyError(state.Error));
    }

    [Fact]
    public void Close_cancel_is_one_shot_even_when_an_earlier_rental_operation_is_pending()
    {
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "租客", Renting = false }));
        Assert.True(state.BeginDeposit(2, 0, 1));
        Assert.True(state.BeginCancelRental(2));
        Assert.Equal(MobileSealRentalState.RentalOperation.Cancel, state.PendingRentalOperation);
        Assert.False(state.BeginCancelRental(3));
        Assert.True(state.ApplyCancelRental());
        Assert.False(state.RentalSessionActive);
    }

    [Fact]
    public void Fixed_layout_keeps_actions_inside_landscape_and_phone_bounds()
    {
        Assert.Equal(6, MobileSealRentalLayout.MaterialPageSize);
        Assert.Equal(9, MobileSealRentalLayout.TargetPageSize);
        Assert.Equal(6, MobileSealRentalLayout.RentalPageSize);

        foreach ((float width, float height) in new[] { (1334F, 750F), (320F, 640F) })
        {
            MobileSealRentalLayout.Bounds panel = MobileSealRentalLayout.GetPanel(width, height);
            Assert.InRange(panel.X, 0F, width);
            Assert.InRange(panel.Y, 0F, height);
            Assert.True(panel.X + panel.Width <= width);
            Assert.True(panel.Y + panel.Height <= height);
            Assert.True(MobileSealRentalLayout.IsReachable(width, height, 12F, 112F + 326F, 80F, 30F));
            Assert.True(MobileSealRentalLayout.IsReachable(width, height, 12F, 112F + 362F, 80F, 60F));
        }
    }

    [Fact]
    public void Default_tab_switches_seal_to_rental_and_back_without_touching_rental_state()
    {
        Assert.Equal(MobileSealRentalLayout.PanelTab.Seal, MobileSealRentalLayout.DefaultTab);
        var state = new MobileSealRentalState();
        Assert.True(state.ApplyRentalRequest(new ServerPackets.ItemRentalRequest { Name = "租客", Renting = false }));

        MobileSealRentalLayout.PanelTab tab = MobileSealRentalLayout.DefaultTab;
        Assert.False(MobileSealRentalLayout.IsTabEnabled(tab, MobileSealRentalLayout.PanelTab.Seal));
        Assert.True(MobileSealRentalLayout.IsTabEnabled(tab, MobileSealRentalLayout.PanelTab.Rental));
        tab = MobileSealRentalLayout.SelectTab(MobileSealRentalLayout.PanelTab.Rental);
        Assert.Equal(MobileSealRentalLayout.PanelTab.Rental, tab);
        Assert.True(MobileSealRentalLayout.IsTabEnabled(tab, MobileSealRentalLayout.PanelTab.Seal));
        Assert.False(MobileSealRentalLayout.IsTabEnabled(tab, MobileSealRentalLayout.PanelTab.Rental));
        tab = MobileSealRentalLayout.SelectTab(MobileSealRentalLayout.PanelTab.Seal);
        Assert.Equal(MobileSealRentalLayout.PanelTab.Seal, tab);
        Assert.True(state.RentalSessionActive);
        Assert.Equal(MobileSealRentalState.RentalOperation.None, state.PendingRentalOperation);
    }
}
