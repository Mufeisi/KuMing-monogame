using System;
using System.Collections.Generic;

namespace Server.Persistence.ChangeTracking
{
    public sealed class EntityChangeTracker<TId>
    {
        private readonly object _gate = new object();
        private readonly Dictionary<TId, EntityChangeKind> _changes;

        public EntityChangeTracker()
            : this(EqualityComparer<TId>.Default)
        {
        }

        public EntityChangeTracker(IEqualityComparer<TId> comparer)
        {
            _changes = new Dictionary<TId, EntityChangeKind>(comparer ?? EqualityComparer<TId>.Default);
        }

        public bool HasChanges
        {
            get
            {
                lock (_gate) return _changes.Count > 0;
            }
        }

        public void MarkAdded(TId id) => Mark(id, EntityChangeKind.Added);

        public void MarkUpdated(TId id) => Mark(id, EntityChangeKind.Updated);

        public void MarkDeleted(TId id) => Mark(id, EntityChangeKind.Deleted);

        public void Clear()
        {
            lock (_gate) _changes.Clear();
        }

        public EntityChangeSet<TId> Drain()
        {
            lock (_gate)
            {
                if (_changes.Count == 0)
                    return new EntityChangeSet<TId>(Array.Empty<TId>(), Array.Empty<TId>(), Array.Empty<TId>());

                var added = new List<TId>();
                var updated = new List<TId>();
                var deleted = new List<TId>();

                foreach (var kvp in _changes)
                {
                    switch (kvp.Value)
                    {
                        case EntityChangeKind.Added:
                            added.Add(kvp.Key);
                            break;
                        case EntityChangeKind.Updated:
                            updated.Add(kvp.Key);
                            break;
                        case EntityChangeKind.Deleted:
                            deleted.Add(kvp.Key);
                            break;
                        default:
                            break;
                    }
                }

                _changes.Clear();

                return new EntityChangeSet<TId>(added, updated, deleted);
            }
        }

        private void Mark(TId id, EntityChangeKind incoming)
        {
            lock (_gate)
            {
                if (!_changes.TryGetValue(id, out var existing))
                {
                    _changes[id] = incoming;
                    return;
                }

                if (TryMerge(existing, incoming, out var merged, out var remove))
                {
                    if (remove)
                    {
                        _changes.Remove(id);
                        return;
                    }

                    _changes[id] = merged;
                }
                else
                {
                    _changes[id] = incoming;
                }
            }
        }

        private static bool TryMerge(EntityChangeKind existing, EntityChangeKind incoming, out EntityChangeKind merged, out bool remove)
        {
            remove = false;
            merged = incoming;

            switch (existing)
            {
                case EntityChangeKind.Added:
                    switch (incoming)
                    {
                        case EntityChangeKind.Added:
                        case EntityChangeKind.Updated:
                            merged = EntityChangeKind.Added;
                            return true;
                        case EntityChangeKind.Deleted:
                            // 同一保存周期内：新增后又删除，等同于无变化。
                            remove = true;
                            return true;
                        default:
                            return false;
                    }

                case EntityChangeKind.Updated:
                    switch (incoming)
                    {
                        case EntityChangeKind.Added:
                        case EntityChangeKind.Updated:
                            merged = EntityChangeKind.Updated;
                            return true;
                        case EntityChangeKind.Deleted:
                            merged = EntityChangeKind.Deleted;
                            return true;
                        default:
                            return false;
                    }

                case EntityChangeKind.Deleted:
                    switch (incoming)
                    {
                        case EntityChangeKind.Added:
                        case EntityChangeKind.Updated:
                            // 删除后又出现写入：视为“恢复/重建”，用 Updated 处理，交由保存逻辑决定如何 upsert。
                            merged = EntityChangeKind.Updated;
                            return true;
                        case EntityChangeKind.Deleted:
                            merged = EntityChangeKind.Deleted;
                            return true;
                        default:
                            return false;
                    }

                default:
                    return false;
            }
        }
    }
}
