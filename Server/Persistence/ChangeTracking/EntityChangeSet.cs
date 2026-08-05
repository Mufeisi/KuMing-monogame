using System;
using System.Collections.Generic;

namespace Server.Persistence.ChangeTracking
{
    public sealed class EntityChangeSet<TId>
    {
        public IReadOnlyList<TId> Added { get; }
        public IReadOnlyList<TId> Updated { get; }
        public IReadOnlyList<TId> Deleted { get; }

        public bool HasChanges => Added.Count > 0 || Updated.Count > 0 || Deleted.Count > 0;

        public EntityChangeSet(IReadOnlyList<TId> added, IReadOnlyList<TId> updated, IReadOnlyList<TId> deleted)
        {
            Added = added ?? throw new ArgumentNullException(nameof(added));
            Updated = updated ?? throw new ArgumentNullException(nameof(updated));
            Deleted = deleted ?? throw new ArgumentNullException(nameof(deleted));
        }
    }
}

