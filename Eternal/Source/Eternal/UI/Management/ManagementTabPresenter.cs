// Relative Path: Eternal/Source/Eternal/UI/Management/ManagementTabPresenter.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Formats a single current management snapshot for the inspect view.
namespace Eternal.UI.Management
{
    public sealed class ManagementTabPresenter
    {
        private readonly ManagementTabModel model;
        public ManagementTabPresenter(ManagementTabModel model) { this.model = model; }
        public ManagementSnapshot Capture() => model.Capture();
        public string Debt(ManagementSnapshot snapshot) => snapshot.Waived ? "Waived" :
            MenuValueCalculator.CompactNumber(snapshot.Debt) + " / " + MenuValueCalculator.CompactNumber(snapshot.Capacity) + " nutrition";
        public string Condition(ManagementCondition condition) => condition.Label + " — " +
            MenuValueCalculator.CompactNumber(condition.Severity) + " severity; " +
            (float.IsNaN(condition.Ticks) ? condition.Status : MenuValueCalculator.Time(condition.Ticks));
        public string ConditionTooltip(ManagementCondition condition) =>
            (condition.Tooltip ?? condition.Status) + (condition.Pricing == null ? "" : "\n" + condition.Pricing);
        public string Part(ManagementPart part) => part.Label + " — " + part.Phase + ": " +
            part.Progress.ToString("P0") + " within phase; " + part.Status;

        public static string CorpseStatus(CorpsePreviewStatus status)
        {
            switch (status)
            {
                case CorpsePreviewStatus.Active: return "Resurrection healing active";
                case CorpsePreviewStatus.WaitingToStart: return "Waiting to start resurrection";
                case CorpsePreviewStatus.Completed: return "Resurrected or corpse removed";
                case CorpsePreviewStatus.Disabled: return "Eternal is disabled";
                default: return "Corpse tracking or healing system unavailable";
            }
        }
    }
}
