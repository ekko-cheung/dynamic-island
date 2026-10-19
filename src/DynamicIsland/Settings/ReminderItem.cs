using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DynamicIsland.Settings;

/// <summary>A scheduled reminder. Times are local wall-clock.</summary>
public sealed partial class ReminderItem : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public bool Daily { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(DueText))] private DateTime _dueAt;

    [JsonIgnore]
    public string DueText
    {
        get
        {
            var time = DueAt.ToString("HH:mm");
            if (Daily) return $"每天 {time}";
            var days = (DueAt.Date - DateTime.Today).Days;
            return days switch { 0 => $"今天 {time}", 1 => $"明天 {time}", _ => $"{DueAt:M月d日} {time}" };
        }
    }
}
