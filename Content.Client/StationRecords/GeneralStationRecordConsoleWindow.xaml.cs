using System.Linq;
using System.Numerics;
using Content.Client.Humanoid;
using Content.Client.Inventory;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
/*using OpenToolkit.Mathematics;*/
using Robust.Client.AutoGenerated;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.StationRecords;

[GenerateTypedNameReferences]
public sealed partial class GeneralStationRecordConsoleWindow : DefaultWindow
{
    public Action<StationRecordKey?>? OnKeySelected;

    public Action<GeneralStationRecordFilterType, string>? OnFiltersChanged;

    private bool _isPopulating;

    private GeneralStationRecordFilterType _currentFilterType;

    private EntityUid _previewDummy; 

    public GeneralStationRecordConsoleWindow()
    {
        RobustXamlLoader.Load(this);

        _currentFilterType = GeneralStationRecordFilterType.Name;

        foreach (var item in Enum.GetValues<GeneralStationRecordFilterType>())
        {
            StationRecordsFilterType.AddItem(GetTypeFilterLocals(item), (int)item);
        }

        RecordListing.OnItemSelected += args =>
        {
            if (_isPopulating || RecordListing[args.ItemIndex].Metadata is not StationRecordKey cast)
            {
                return;
            }

            OnKeySelected?.Invoke(cast);
        };

        RecordListing.OnItemDeselected += _ =>
        {
            if (!_isPopulating)
                OnKeySelected?.Invoke(null);
        };

        StationRecordsFilterType.OnItemSelected += eventArgs =>
        {
            var type = (GeneralStationRecordFilterType)eventArgs.Id;

            if (_currentFilterType != type)
            {
                _currentFilterType = type;
                FilterListingOfRecords();
            }
        };

        StationRecordsFiltersValue.OnTextEntered += args =>
        {
            FilterListingOfRecords(args.Text);
        };

        StationRecordsFilters.OnPressed += _ =>
        {
            FilterListingOfRecords(StationRecordsFiltersValue.Text);
        };

        StationRecordsFiltersReset.OnPressed += _ =>
        {
            StationRecordsFiltersValue.Text = "";
            FilterListingOfRecords();
        };
    }

    public void UpdateState(GeneralStationRecordConsoleState state)
    {
        if (state.Filter != null)
        {
            if (state.Filter.Type != _currentFilterType)
            {
                _currentFilterType = state.Filter.Type;
            }

            if (state.Filter.Value != StationRecordsFiltersValue.Text)
            {
                StationRecordsFiltersValue.Text = state.Filter.Value;
            }
        }

        StationRecordsFilterType.SelectId((int)_currentFilterType);

        if (state.RecordListing == null)
        {
            RecordListingStatus.Visible = true;
            RecordListing.Visible = false;
            RecordListingStatus.Text = Loc.GetString("general-station-record-console-empty-state");
            RecordContainer.Visible = false;
            RecordContainerStatus.Visible = false;
            return;
        }

        RecordListingStatus.Visible = false;
        RecordListing.Visible = true;
        RecordContainer.Visible = true;

        PopulateRecordListing(state.RecordListing!, state.SelectedKey);

        RecordContainerStatus.Visible = state.Record == null;

        if (state.Record != null)
        {
            RecordContainerStatus.Visible = state.SelectedKey == null;
            RecordContainerStatus.Text = state.SelectedKey == null
                ? Loc.GetString("general-station-record-console-no-record-found")
                : Loc.GetString("general-station-record-console-select-record-info");
            PopulateRecordContainer(state.Record);
        }
        else
        {
            RecordContainer.DisposeAllChildren();
            RecordContainer.RemoveAllChildren();
        }
    }
    private void PopulateRecordListing(Dictionary<(NetEntity, uint), string> listing, (NetEntity, uint)? selected)
    {
        RecordListing.Clear();
        RecordListing.ClearSelected();

        _isPopulating = true;

        foreach (var (key, name) in listing)
        {
            var item = RecordListing.AddItem(name);
            item.Metadata = key;
            if (selected != null && key.Item1 == selected.Value.Item1 && key.Item2 == selected.Value.Item2)
            {
                item.Selected = true;
            }
        }
        _isPopulating = false;

        RecordListing.SortItemsByText();
    }

    private void PopulateRecordContainer(GeneralStationRecord record)
    {
        RecordContainer.DisposeAllChildren();
        RecordContainer.RemoveAllChildren();

        var recordControls = new Control[]
        {
            new Label()
            {
                Text = record.Name,
                StyleClasses = { "LabelBig" }
            },
            SetupCharacterSpriteView(record),
            new Label()
            {
                Text = Loc.GetString("general-station-record-console-record-age", ("age", record.Age.ToString()))

            },
            new Label()
            {
                Text = Loc.GetString("general-station-record-console-record-title", ("job", Loc.GetString(record.JobTitle)))
            },
            new Label()
            {
                Text = Loc.GetString("general-station-record-console-record-species", ("species", record.Species))
            },
            new Label()
            {
                Text = Loc.GetString("general-station-record-console-record-gender", ("gender", record.Gender.ToString()))
            },
            new Label()
            {
                Text = Loc.GetString("general-station-record-console-record-fingerprint", ("fingerprint", record.Fingerprint ?? Loc.GetString("generic-not-available-shorthand")))
            },
            new Label()
            {
                Text = Loc.GetString("general-station-record-console-record-dna", ("dna", record.DNA ?? Loc.GetString("generic-not-available-shorthand")))
            }
        };

        foreach (var control in recordControls)
        {
            RecordContainer.AddChild(control);
        }
    }

    private BoxContainer SetupCharacterSpriteView(GeneralStationRecord record)
    {
        IEntityManager entityManager = IoCManager.Resolve<IEntityManager>();
        IPrototypeManager prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        HumanoidAppearanceSystem appearanceSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<HumanoidAppearanceSystem>();
        
        entityManager.DeleteEntity(_previewDummy);
        
        var profile = record.Profile ?? new HumanoidCharacterProfile();
        _previewDummy = entityManager.SpawnEntity(prototypeManager.Index<SpeciesPrototype>(profile.Species).DollPrototype, MapCoordinates.Nullspace);
        appearanceSystem.LoadProfile(_previewDummy, profile);
        GiveDummyJobClothes(_previewDummy, record.JobPrototype, profile);

        var spriteViewBox = new BoxContainer();
        var sprite = entityManager.GetComponent<SpriteComponent>(_previewDummy);
        
        spriteViewBox.AddChild(new SpriteView() { Sprite = sprite, Scale = new Vector2(5, 5)});
        spriteViewBox.AddChild(new SpriteView() { Sprite = sprite, Scale = new Vector2(5, 5), OverrideDirection = Direction.East});

        return spriteViewBox;
    }
    private void GiveDummyJobClothes(EntityUid dummy, string jobPrototype, HumanoidCharacterProfile profile)
    {
        IEntityManager entityManager = IoCManager.Resolve<IEntityManager>();
        IPrototypeManager prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        ClientInventorySystem inventorySystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<ClientInventorySystem>();
        
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract (what is resharper smoking?)
        var job = prototypeManager.Index<JobPrototype>(jobPrototype ?? SharedGameTicker.FallbackOverflowJob);

        if (job.StartingGear != null && inventorySystem.TryGetSlots(dummy, out var slots))
        {
            var gear = prototypeManager.Index<StartingGearPrototype>(job.StartingGear);

            foreach (var slot in slots)
            {
                var itemType = gear.GetGear(slot.Name, profile);
                if (inventorySystem.TryUnequip(dummy, slot.Name, out var unequippedItem, true, true))
                {
                    entityManager.DeleteEntity(unequippedItem.Value);
                }

                if (itemType != string.Empty)
                {
                    var item = entityManager.SpawnEntity(itemType, MapCoordinates.Nullspace);
                    inventorySystem.TryEquip(dummy, item, slot.Name, true, true);
                }
            }
        }
    }

    private void FilterListingOfRecords(string text = "")
    {
        if (!_isPopulating)
        {
            OnFiltersChanged?.Invoke(_currentFilterType, text);
        }
    }

    private string GetTypeFilterLocals(GeneralStationRecordFilterType type)
    {
        return Loc.GetString($"general-station-record-{type.ToString().ToLower()}-filter");
    }
}
