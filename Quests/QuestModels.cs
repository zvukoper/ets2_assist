using System;
using System.Collections.Generic;

namespace ETS2_Assist_GUI.Quests
{
    public enum QuestStatus
    {
        Available,
        Active,
        Completed,
        Cancelled,
        Failed,
        Archived
    }

    public sealed class QuestDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public QuestRequirement? Requirements { get; set; }
        public List<string> Excludes { get; set; } = new();
        public List<QuestInteractionDefinition> Interactions { get; set; } = new();
        public Dictionary<string, QuestDialogueNode> Dialogues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, QuestStepDefinition> Steps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<QuestReward> Rewards { get; set; } = new();
    }

    public sealed class QuestStepDefinition
    {
        public string Id { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public sealed class QuestInteractionDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public QuestPointSource Source { get; set; } = new();
        public double TriggerRadiusM { get; set; } = 35;
        public bool MinimapVisible { get; set; } = true;
        public bool ArVisible { get; set; } = true;
        public string DefaultMarker { get; set; } = "none";
        public string ActiveMarker { get; set; } = "none";
        public string CompletedMarker { get; set; } = "none";
        public string RequiredQuestStatus { get; set; } = "";
        public string RequiredQuestStep { get; set; } = "";
        public bool PermanentName { get; set; }
        public string CompletedName { get; set; } = "";
        public string InitialDialogue { get; set; } = "";
        public string ActiveDialogue { get; set; } = "";
        public string CompletedDialogue { get; set; } = "";
        public string CancelledDialogue { get; set; } = "";
        public bool ShowWhenQuestCompleted { get; set; }
    }

    public sealed class QuestPointSource
    {
        public string Category { get; set; } = "";
        public string Uid { get; set; } = "";
        public QuestPointSelector? Selector { get; set; }
    }

    public sealed class QuestPointSelector
    {
        public string Category { get; set; } = "";
        public double MinDistanceM { get; set; }
        public double MaxDistanceM { get; set; }
        public double MaxRoadDistanceM { get; set; } = 80;
        public string AnchorInteractionId { get; set; } = "";
    }

    public sealed class QuestDialogueNode
    {
        public string Speaker { get; set; } = "";
        public string Text { get; set; } = "";
        public string Image { get; set; } = "";
        public List<QuestDialogueOption> Options { get; set; } = new();
    }

    public sealed class QuestDialogueOption
    {
        public string Text { get; set; } = "";
        public QuestRequirement? Requirements { get; set; }
        public string Next { get; set; } = "";
        public bool Close { get; set; }
        public List<QuestEffect> Effects { get; set; } = new();
    }

    public sealed class QuestRequirement
    {
        public List<QuestCondition> All { get; set; } = new();
        public List<QuestCondition> Any { get; set; } = new();
        public List<QuestCondition> None { get; set; } = new();
    }

    public sealed class QuestCondition
    {
        public string QuestId { get; set; } = "";
        public string QuestStatus { get; set; } = "";
        public string QuestStep { get; set; } = "";
        public string Item { get; set; } = "";
        public int Amount { get; set; }
        public string Reputation { get; set; } = "";
        public int MinValue { get; set; }
        public string Stat { get; set; } = "";
        public int MinStatValue { get; set; }
        public string Flag { get; set; } = "";
        public bool FlagValue { get; set; }
    }

    public sealed class QuestEffect
    {
        public string SetQuestStatus { get; set; } = "";
        public string SetStep { get; set; } = "";
        public string Item { get; set; } = "";
        public int AddItem { get; set; }
        public int RemoveItem { get; set; }
        public string Reputation { get; set; } = "";
        public int AddReputation { get; set; }
        public string Stat { get; set; } = "";
        public int AddStat { get; set; }
        public string Flag { get; set; } = "";
        public bool? SetFlag { get; set; }
        public string RenameInteraction { get; set; } = "";
        public string RenameTo { get; set; } = "";
        public string NotifyTitle { get; set; } = "";
        public string NotifyText { get; set; } = "";
    }

    public sealed class QuestReward
    {
        public string Type { get; set; } = "";
        public string Id { get; set; } = "";
        public int Amount { get; set; }
        public string Display { get; set; } = "";
        public string Color { get; set; } = "";
    }

    public sealed class QuestProgress
    {
        public QuestStatus Status { get; set; } = QuestStatus.Available;
        public string Step { get; set; } = "";
        public bool ReturnOffer { get; set; }
        public Dictionary<string, bool> Flags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class QuestPersistentState
    {
        public Dictionary<string, QuestProgress> Quests { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Inventory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Reputation { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Stats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PermanentInteractionNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class QuestSettings
    {
        public bool Enabled { get; set; } = true;
        public bool DebugShowAllPoints { get; set; }
        public double DebugRadiusM { get; set; } = 50;
        public double TriggerRadiusM { get; set; } = 35;
        public double ArSizeMaxDistanceM { get; set; } = 10;
        public double ArSizeMinDistanceM { get; set; } = 500;
        public double ArFadeStartDistanceM { get; set; } = 500;
        public double ArFadeEndDistanceM { get; set; } = 1500;
        public double ArMaxPointSizePx { get; set; } = 10;
        public double ArMinPointSizePx { get; set; } = 3;
        public double ArNearOutlinePx { get; set; } = 3;
        public double ArFarOutlinePx { get; set; } = 1;
        public double NotificationWidthPercent { get; set; } = 32.4074;
        public double NotificationHeightPercent { get; set; } = 3.7037;
        public int PollIntervalMs { get; set; } = 250;
    }

    public sealed class QuestPointSnapshot
    {
        public string QuestId { get; set; } = "";
        public string InteractionId { get; set; } = "";
        public string Uid { get; set; } = "";
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public string Marker { get; set; } = "none";
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public bool MinimapVisible { get; set; }
        public bool ArVisible { get; set; }
        public bool Interactive { get; set; }
        public bool PermanentName { get; set; }
        public double TriggerRadiusM { get; set; }
    }
}