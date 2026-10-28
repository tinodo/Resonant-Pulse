using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ResonantPulse.App.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private static readonly string[] LaneSounds = ["Wood", "Glass", "Click", "Kick", "HiHat"];
    private static readonly IReadOnlySet<int> NoGroups = new HashSet<int>();

    /// <summary>One beat of a lane's strength strip: a 44 DIP wide button.</summary>
    private const double LaneBeatCell = 44;

    /// <summary>
    /// Applies one change to a Studio field. It is queued behind the edits before it, so quick edits are applied in the order
    /// they were made, and the page is not rebuilt: the field already shows the value. If the project changed under this page
    /// (a lane was removed) the page is refreshed and the edit refused, as for any editor command. A locked project refuses it
    /// too; that is reported, and the caller puts the field back. If the person left Studio and came back while the edit waited,
    /// the new page was built from the project as it was before the edit, so it is built again once the edit is in.
    /// </summary>
    private async Task CommitStudioAsync(int revision, string coalesceKey, Action<PulseDocument> edit)
    {
        var generation = _pageGeneration;
        var applied = false;
        try
        {
            await CommitAsync(doc =>
            {
                edit(doc);
                applied = true;
            }, expectedRevision: revision, editorChange: EditorChangeKind.InPlaceField, coalesceKey: coalesceKey);
        }
        catch (EditorRevisionChangedException)
        {
            if (!_isClosed) RebuildPreservingScroll();
            throw;
        }
        finally
        {
            // Also when the recovery write failed after the edit was published: the project holds the edit either way.
            if (applied && generation != _pageGeneration && _view == "Studio" && !_isClosed) RebuildPreservingScroll();
        }
        if (!applied) throw new OperationCanceledException();
    }

    /// <summary>A mute or solo switch for a lane: a toggle button that shows its state and says what it does.</summary>
    private ToggleButton LaneToggle(string labelKey, string id, string accessibleName, string helpKey, bool isOn,
        Func<bool> current, Func<bool, Task> commit)
    {
        var toggle = new ToggleButton
        {
            IsChecked = isOn, MinHeight = 44, MinWidth = 44, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 0, 16, 0),
            Content = new TextBlock { Text = Text.Get(labelKey), TextWrapping = TextWrapping.NoWrap }
        };
        Identify(toggle, id, accessibleName);
        ToolTipService.SetToolTip(toggle, Text.Get(helpKey));
        AutomationProperties.SetHelpText(toggle, Text.Get(helpKey));
        var latest = 0;
        toggle.Click += async (_, _) =>
        {
            var mine = ++latest;
            var wanted = toggle.IsChecked == true;
            var applied = false;
            await GuardAsync(async () =>
            {
                await commit(wanted);
                applied = true;
            });
            if (!applied && mine == latest) toggle.IsChecked = current();
        };
        return toggle;
    }

    /// <summary>
    /// One lane as a card: its name with mute, solo and more actions, then Rhythm (the strength of each beat, clicks per
    /// cycle, cycle length, offset), Sound (sound, volume, pan) and Variation (chance). Every field applies as it is changed
    /// and nothing is rebuilt, so the focus, the caret and the scroll position stay where they are. Adding, removing,
    /// duplicating and rotating are commands in the More menu: they change what is on the page, so the page is rebuilt.
    /// </summary>
    private UIElement BuildLaneCard(int index)
    {
        var revision = _editorContentRevision;
        var id = $"Lane{index}";
        var lane = _document.Lanes[index];
        var exact = lane.Events.Count > 0;

        // The project can move on (a lane removed) before this page is replaced, so reads never assume the lane is still there.
        T Read<T>(Func<RhythmLane, T> read, T fallback) => index < _document.Lanes.Count ? read(_document.Lanes[index]) : fallback;
        Task Change(string field, Action<RhythmLane> edit) =>
            CommitStudioAsync(revision, $"{id}.{field}", doc => edit(doc.Lanes[index]));
        string Named(string captionKey) => Text.Format("LaneField", index + 1, Text.Get(captionKey));
        // The visible caption stays as it is; the name a screen reader hears says which lane the field belongs to.
        void Qualify(FrameworkElement control, string captionKey, string? unitKey = null) =>
            AutomationProperties.SetName(control, unitKey is null ? Named(captionKey) : $"{Named(captionKey)} ({Text.Get(unitKey)})");

        // ---- header: name, mute, solo, more --------------------------------------------------------------------------
        var name = EditorControl(TextInput("Name", id + "Name", lane.Name));
        // A name is as wide as a name needs, and gives way in a narrow card (the field host); not as wide as the card.
        name.MaxWidth = double.PositiveInfinity;
        name.Width = 280;
        name.HorizontalAlignment = HorizontalAlignment.Left;
        name.MaxLength = LaneEdits.MaxNameLength;
        CommitText(name, () => Read(item => item.Name, name.Text), async text =>
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowNotice("LaneNameRequired");
                throw new OperationCanceledException();
            }
            await Change("Name", item => item.Name = text);
        });
        var nameField = Field(name);
        AutomationProperties.SetName(name, Named("Name"));
        TrackStudioFocus(id + "Name", name);

        var mute = EditorControl(LaneToggle("Mute", id + "Muted", Named("Mute"), "LaneMuteHelp", lane.Muted,
            () => Read(item => item.Muted, false), on => Change("Muted", item => item.Muted = on)));
        var solo = EditorControl(LaneToggle("LaneSolo", id + "Solo", Named("LaneSolo"), "LaneSoloHelp", lane.Solo,
            () => Read(item => item.Solo, false), on => Change("Solo", item => item.Solo = on)));
        var more = new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 16 }, MinWidth = 44, Padding = new Thickness(0) };
        Identify(more, id + "More", Named("LaneMore"));
        ToolTipService.SetToolTip(more, Text.Get("LaneMore"));
        TrackStudioFocus(id + "More", more);
        var menu = new MenuFlyout();
        void AddCommand(string key, string commandId, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = Text.Get(key) };
            AutomationProperties.SetAutomationId(item, commandId);
            item.Click += async (_, _) => await GuardAsync(() => RunEditorOperationAsync(revision, action));
            menu.Items.Add(item);
        }
        AddCommand("Rotate", id + "Rotate", async () =>
        {
            await CommitEditorAsync(doc => LaneEdits.Rotate(doc.Lanes[index]));
            FocusAfterRebuild(id + "More");
            RebuildPreservingScroll();
        });
        AddCommand("Invert", id + "Invert", async () =>
        {
            await CommitEditorAsync(doc => LaneEdits.Invert(doc.Lanes[index]));
            FocusAfterRebuild(id + "More");
            RebuildPreservingScroll();
        });
        AddCommand("Duplicate", id + "Duplicate", async () =>
        {
            await CommitEditorAsync(doc =>
            {
                if (doc.Lanes.Count >= LaneEdits.MaxLanes) throw new ArgumentException(Text.Get("LaneLimit"));
                // The copy goes last: lanes before it keep their place, and so the random pattern behind their Chance.
                var copy = LocalWorkspace.Clone(doc).Lanes[index];
                copy.Name = LaneEdits.CopyName(copy.Name, Text.Get("LaneCopySuffix"));
                doc.Lanes.Add(copy);
            });
            FocusAfterRebuild($"Lane{_document.Lanes.Count - 1}Name");
            RebuildPreservingScroll();
        });
        AddCommand("Remove", id + "Remove", async () =>
        {
            if (!await ConfirmAsync(Text.Format("ConfirmRemove", Read(item => item.Name, lane.Name)), "Remove")) return;
            await CommitEditorAsync(doc => doc.Lanes.RemoveAt(index));
            // The next lane takes the place of the one removed; after the last, the one before it; with none left, Add lane.
            FocusAfterRebuild(_document.Lanes.Count == 0 ? "AddLane" : $"Lane{Math.Min(index, _document.Lanes.Count - 1)}Name");
            RebuildPreservingScroll();
        });
        more.Flyout = menu;
        var commands = CommandRow(mute, solo, more);
        commands.VerticalAlignment = VerticalAlignment.Bottom;
        var header = Wrapped(16, 8, [nameField, commands]);

        // ---- Rhythm: strength of each beat, then the numbers ----------------------------------------------------------
        var strip = new BeatStrip
        {
            AutomationPrefix = id + "Step", RovingFocus = true, HorizontalAlignment = HorizontalAlignment.Left,
            Describe = (step, level) => Text.Format("LaneBeatName", index + 1, step + 1, Text.Get("Level" + level))
        };
        var stripScroll = EditorControl(new ScrollViewer
        {
            Content = strip, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        Identify(stripScroll, id + "Strip", Named("PulseStrengths"));
        var scrollCue = Note("ScrollControlsHint");
        scrollCue.Visibility = Visibility.Collapsed;
        void FitStrip()
        {
            // Judged from the data, not from the scroll bar the cue itself could create. While it scrolls, the strip leaves
            // room for the bar, so the bar never covers the beat numbers.
            var scrolls = stripScroll.ActualWidth > 0 && strip.Count * LaneBeatCell > stripScroll.ActualWidth;
            scrollCue.Visibility = scrolls ? Visibility.Visible : Visibility.Collapsed;
            strip.Margin = new Thickness(0, 0, 0, scrolls ? PlayLayout.StripScrollBarRoom : 0);
        }
        stripScroll.SizeChanged += (_, _) => FitStrip();
        // The strip shows what the project holds, so it is brought in line after every change to a beat or to the clicks.
        void RefreshStrip()
        {
            if (index >= _document.Lanes.Count || _document.Lanes[index].Events.Count > 0) return;
            var current = _document.Lanes[index];
            var levels = AccentPatterns.ExpandLane(current.Pattern, current.Pulses);
            if (strip.Count != levels.Length) strip.Rebuild(levels.Length, NoGroups, LaneBeatCell, levels);
            else strip.SetLevels(levels);
            FitStrip();
        }
        Task ChangeBeat(int step, int delta, bool wrap) => GuardAsync(async () =>
        {
            // A beat that is gone (the number of clicks was lowered a moment ago) is not changed: nothing is applied.
            try
            {
                await Change($"Beat{step}", item =>
                {
                    if (!LaneEdits.ChangeAccent(item, step, delta, wrap)) throw new OperationCanceledException();
                });
            }
            finally { RefreshStrip(); }
        });
        strip.BeatInvoked += async (_, step) => await ChangeBeat(step, 1, wrap: true);
        strip.BeatNudged += async (_, args) => await ChangeBeat(args.Index, args.Delta, wrap: false);
        if (!exact) strip.Rebuild(lane.Pulses, NoGroups, LaneBeatCell, AccentPatterns.ExpandLane(lane.Pattern, lane.Pulses));

        var pulses = EditorControl(NumberInput("LanePulses", id + "Pulses", lane.Pulses, 1, 256));
        CommitNumber(pulses, () => Read(item => (double)item.Pulses, pulses.Value), async value =>
        {
            var count = Integer(value);
            try { await Change("Pulses", item => LaneEdits.SetPulses(item, count)); }
            finally { RefreshStrip(); }
        });
        var cycle = EditorControl(NumberInput("LaneCycle", id + "Cycle", lane.CycleQuarters, 1d / 1024, 4096, 0.125, 3, whole: false, trimmed: true));
        CommitNumber(cycle, () => Read(item => item.CycleQuarters, cycle.Value),
            value => Change("Cycle", item => item.CycleQuarters = value));
        var offset = EditorControl(NumberInput("LaneOffset", id + "Offset", lane.OffsetQuarters, -4096, 4096, 0.125, 3, whole: false, trimmed: true));
        CommitNumber(offset, () => Read(item => item.OffsetQuarters, offset.Value),
            value => Change("Offset", item => item.OffsetQuarters = value));
        var pulsesField = Field(pulses);
        var cycleField = Field(cycle, "UnitQuarterNotes");
        var offsetField = Field(offset, "UnitQuarterNotes");
        Qualify(pulses, "LanePulses");
        Qualify(cycle, "LaneCycle", "UnitQuarterNotes");
        Qualify(offset, "LaneOffset", "UnitQuarterNotes");

        // ---- Sound and Variation ------------------------------------------------------------------------------------------
        var sound = EditorControl(Dropdown("LaneSoundCaption", id + "Sound", lane.Sound, LaneSounds.Select(item => new Choice(item, Text.Get(item))).ToArray()));
        CommitChoice(sound, () => Read(item => item.Sound, lane.Sound), value => Change("Sound", item => item.Sound = value));
        var soundField = Field(sound);
        Qualify(sound, "LaneSoundCaption");
        string Percent(int value) => Text.Format("PercentValue", value);
        string Pan(int value) => value == 0 ? Text.Get("PanCenter") : value < 0 ? Text.Format("PanLeft", -value) : Text.Format("PanRight", value);
        var volumeField = SliderField("Volume", id + "Volume", Named("Volume"), LevelFormat.ToPercent(lane.Volume), 0, 100,
            Percent, [Percent(100)], () => LevelFormat.ToPercent(Read(item => (double)item.Volume, 0)),
            value => Change("Volume", item => item.Volume = (float)LevelFormat.FromPercent(value)));
        var panField = SliderField("LanePan", id + "Pan", Named("LanePan"), LevelFormat.PanToPercent(lane.Pan), -100, 100,
            Pan, [Pan(-100), Pan(100), Pan(0)], () => LevelFormat.PanToPercent(Read(item => (double)item.Pan, 0)),
            value => Change("Pan", item => item.Pan = (float)LevelFormat.PanFromPercent(value)));
        var chanceField = SliderField("LaneChance", id + "Probability", Named("LaneChance"), LevelFormat.ToPercent(lane.Probability), 0, 100,
            Percent, [Percent(100)], () => LevelFormat.ToPercent(Read(item => item.Probability, 1)),
            value => Change("Probability", item => item.Probability = LevelFormat.FromPercent(value)));

        // ---- exact positions -------------------------------------------------------------------------------------------------
        var positions = new Expander
        {
            Header = Text.Get("CustomPositions"), Content = BuildExactEvents(index), IsExpanded = exact,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Identify(positions, id + "Positions", Named("CustomPositions"));

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(header);
        body.Children.Add(SectionHeading("LaneGroupRhythm"));
        if (exact) body.Children.Add(Note("LaneExactNote"));
        else
        {
            var caption = new TextBlock { Text = Text.Get("BeatStrengthCaption"), Style = ResonantStyle("FieldCaptionStyle") };
            body.Children.Add(new StackPanel { Spacing = 4, Children = { caption, stripScroll, scrollCue } });
        }
        body.Children.Add(exact ? FieldRow(cycleField, offsetField) : FieldRow(pulsesField, cycleField, offsetField));
        body.Children.Add(Note("LaneRhythmHelp"));
        body.Children.Add(SectionHeading("Sound"));
        body.Children.Add(FieldRow(soundField, volumeField, panField));
        body.Children.Add(SectionHeading("LaneGroupVariation"));
        body.Children.Add(chanceField);
        body.Children.Add(Note("LaneChanceHelp"));
        body.Children.Add(positions);
        var card = Card(null, body);
        Identify(card, id + "Card", $"{Text.Get("Lane")} {index + 1}");
        return card;
    }
}
