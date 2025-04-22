using System.ComponentModel;

namespace MVCMediaShareAppNew.Enums
{
    public enum BlogCreationState
    {
        [Description("Draft")]
        Draft,

        [Description("Published")]
        Published,

        [Description("Archived")]
        Archived
    }
}