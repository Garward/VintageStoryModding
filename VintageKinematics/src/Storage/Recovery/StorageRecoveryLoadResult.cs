using System;
using System.Collections.Generic;

namespace VintageKinematics.Storage.Recovery
{
    public sealed class StorageRecoveryLoadResult
    {
        private readonly IReadOnlyList<StorageRecoveryLoadIssue> issues;

        public StorageRecoveryRegistry Registry { get; }
        public IReadOnlyList<StorageRecoveryLoadIssue> Issues => issues;
        public bool CanPersist => issues.Count == 0;

        internal StorageRecoveryLoadResult(
            StorageRecoveryRegistry registry,
            IReadOnlyList<StorageRecoveryLoadIssue> issues)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (issues == null)
            {
                this.issues = Array.Empty<StorageRecoveryLoadIssue>();
            }
            else
            {
                StorageRecoveryLoadIssue[] copy = new StorageRecoveryLoadIssue[issues.Count];
                for (int i = 0; i < issues.Count; i++) copy[i] = issues[i];
                this.issues = copy;
            }
        }
    }
}
