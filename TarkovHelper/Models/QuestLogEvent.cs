namespace TarkovHelper.Models
{
    /// <summary>
    /// Type of quest event from game logs
    /// </summary>
    public enum QuestEventType
    {
        /// <summary>
        /// Quest started (message.type == 10)
        /// </summary>
        Started,

        /// <summary>
        /// Quest completed successfully (message.type == 12)
        /// </summary>
        Completed,

        /// <summary>
        /// Quest failed (message.type == 11)
        /// </summary>
        Failed
    }

    /// <summary>
    /// Represents a quest event parsed from game logs
    /// </summary>
    public class QuestLogEvent
    {
        /// <summary>
        /// Quest ID from templateId (first token)
        /// </summary>
        public string QuestId { get; set; } = string.Empty;

        /// <summary>
        /// Type of event (Started, Completed, Failed)
        /// </summary>
        public QuestEventType EventType { get; set; }

        /// <summary>
        /// Trader ID from dialogId
        /// </summary>
        public string TraderId { get; set; } = string.Empty;

        /// <summary>
        /// Event timestamp from dt field (Unix timestamp converted to DateTime)
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Original log line for debugging
        /// </summary>
        public string? OriginalLine { get; set; }

        /// <summary>
        /// Source log file name
        /// </summary>
        public string? SourceFile { get; set; }
    }

    /// <summary>
    /// Result of quest log synchronization
    /// </summary>
    public class SyncResult
    {
        /// <summary>
        /// Total quest events found in logs
        /// </summary>
        public int TotalEventsFound { get; set; }

        /// <summary>
        /// Quests marked as started
        /// </summary>
        public int QuestsStarted { get; set; }

        /// <summary>
        /// Quests marked as completed (from log events)
        /// </summary>
        public int QuestsCompleted { get; set; }

        /// <summary>
        /// Quests marked as failed
        /// </summary>
        public int QuestsFailed { get; set; }

        /// <summary>
        /// Prerequisites auto-completed due to started quests
        /// </summary>
        public int PrerequisitesAutoCompleted { get; set; }

        /// <summary>
        /// Quest IDs that couldn't be matched to TarkovTask
        /// </summary>
        public List<string> UnmatchedQuestIds { get; set; } = new();

        /// <summary>
        /// Errors encountered during sync
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// List of quests that will be completed (for confirmation dialog)
        /// </summary>
        public List<QuestChangeInfo> QuestsToComplete { get; set; } = new();

        /// <summary>
        /// List of quests that are currently in progress (started but not completed/failed)
        /// </summary>
        public List<TarkovTask> InProgressQuests { get; set; } = new();

        /// <summary>
        /// List of quests that were completed (from log events)
        /// </summary>
        public List<TarkovTask> CompletedQuests { get; set; } = new();

        /// <summary>
        /// Whether the sync was successful overall
        /// </summary>
        public bool Success => Errors.Count == 0 || TotalEventsFound > 0;
    }

    /// <summary>
    /// Information about a quest change for the confirmation dialog
    /// </summary>
    public class QuestChangeInfo
    {
        /// <summary>
        /// Quest display name
        /// </summary>
        public string QuestName { get; set; } = string.Empty;

        /// <summary>
        /// Quest normalized name for identification
        /// </summary>
        public string NormalizedName { get; set; } = string.Empty;

        /// <summary>
        /// Trader name
        /// </summary>
        public string Trader { get; set; } = string.Empty;

        /// <summary>
        /// Whether this is a prerequisite auto-completion
        /// </summary>
        public bool IsPrerequisite { get; set; }

        /// <summary>
        /// The change type (Completed, Failed, Started)
        /// </summary>
        public QuestEventType ChangeType { get; set; }

        /// <summary>
        /// Whether this quest is selected for completion
        /// </summary>
        public bool IsSelected { get; set; } = true;

        /// <summary>
        /// Event timestamp for chronological ordering
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Formatted timestamp for display
        /// </summary>
        public string FormattedTimestamp => Timestamp != default
            ? Timestamp.ToString("MM/dd HH:mm")
            : "";
    }
}
