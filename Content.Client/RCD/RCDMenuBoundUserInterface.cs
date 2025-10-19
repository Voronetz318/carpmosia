using Content.Client.Popups;
using Content.Client.UserInterface.Controls;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Collections;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.RCD;

[UsedImplicitly]
public sealed class RCDMenuBoundUserInterface : BoundUserInterface
{
    private const string TopLevelActionCategory = "Main";

    private static readonly Dictionary<string, (string Tooltip, SpriteSpecifier Sprite)> PrototypesGroupingInfo
        = new Dictionary<string, (string Tooltip, SpriteSpecifier Sprite)>
        {
            ["WallsAndFlooring"] = ("rcd-component-walls-and-flooring", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/walls_and_flooring.png"))),
            ["WindowsAndGrilles"] = ("rcd-component-windows-and-grilles", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/windows_and_grilles.png"))),
            ["Airlocks"] = ("rcd-component-airlocks", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/airlocks.png"))),
            ["Electrical"] = ("rcd-component-electrical", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/multicoil.png"))),
            ["Lighting"] = ("rcd-component-lighting", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/lighting.png"))),
            // Carpmosia-start - Add RPD
            ["Piping"] = ("rcd-component-piping", new SpriteSpecifier.Rsi(new ResPath("Structures/Piping/Atmospherics/pipe.rsi"), "pipeFourway")),
            ["AtmosphericUtility"] = ("rcd-component-atmosphericutility", new SpriteSpecifier.Rsi(new ResPath("Structures/Piping/Atmospherics/gasmixer.rsi"), "gasMixer")),
            ["PumpsValves"] = ("rcd-component-pumpsvalves", new SpriteSpecifier.Rsi(new ResPath("Structures/Piping/Atmospherics/pump.rsi"), "pumpVolume")),
            ["Vents"] = ("rcd-component-vents", new SpriteSpecifier.Rsi(new ResPath("Structures/Piping/Atmospherics/vent.rsi"), "vent_passive")),
            // Carpmosia-end - Add RPD
        };

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;

    private SimpleRadialMenu? _menu;

    public RCDMenuBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        if (!EntMan.TryGetComponent<RCDComponent>(Owner, out var rcd))
            return;

        _menu = this.CreateWindow<SimpleRadialMenu>();
        _menu.Track(Owner);
        var models = ConvertToButtons(rcd.AvailablePrototypes);
        _menu.SetButtons(models);

        _menu.OpenOverMouseScreenPosition();
    }

    private IEnumerable<RadialMenuOptionBase> ConvertToButtons(HashSet<ProtoId<RCDPrototype>> prototypes)
    {
        Dictionary<string, List<RadialMenuActionOptionBase>> buttonsByCategory = new();
        ValueList<RadialMenuActionOptionBase> topLevelActions = new();
        // Iterate through the prototypes to build the list of actionOptions
        // There is a distinction between prototypes from the top level category,
        // and those from other categories, regarding how they are processed and turned into actionOptions
        foreach (var protoId in prototypes)
        {
            // Get the prototype object with this specific protoId
            var prototype = _prototypeManager.Index(protoId);
            // Check if the prototype is from the top level category
            if (prototype.Category == TopLevelActionCategory)
            {
                // Create the actionOption for it and add that to the list
                var topLevelActionOption = new RadialMenuActionOption<RCDPrototype>(HandleMenuOptionClick, prototype)
                {
                    IconSpecifier = RadialMenuIconSpecifier.With(prototype.Sprite),
                    ToolTip = GetTooltip(prototype)
                };
                topLevelActions.Add(topLevelActionOption);
                continue;
            }

            // Check if the prototype's category is present in PrototypesGroupingInfo
            // If it is not, then it is an unknown category, and it can be skipped
            if (!PrototypesGroupingInfo.TryGetValue(prototype.Category, out var groupInfo))
                continue;

            // Check if the prototype's category is present in buttonsByCategory
            // If it is not, a new entry associated to that category is added
            if (!buttonsByCategory.TryGetValue(prototype.Category, out var list))
            {
                list = new List<RadialMenuActionOptionBase>();
                buttonsByCategory.Add(prototype.Category, list);
            }

            // Make an actionOption based on the prototype
            var actionOption = new RadialMenuActionOption<RCDPrototype>(HandleMenuOptionClick, prototype)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(prototype.Sprite),
                ToolTip = GetTooltip(prototype)
            };
            // Add it to the buttonsByCategory entry associated with the prototype's category
            list.Add(actionOption);
        }

        // models is the array of actionOptions that is initially presented to the client
        // It contains the nested actionOptions from buttonsByCategory entries, and the top level category ones
        var models = new RadialMenuOptionBase[buttonsByCategory.Count + topLevelActions.Count];
        var i = 0;
        // Iterate through the buttonsByCategory entries and add them in nested actionOptions
        foreach (var (key, list) in buttonsByCategory)
        {
            var groupInfo = PrototypesGroupingInfo[key];
            models[i] = new RadialMenuNestedLayerOption(list)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(groupInfo.Sprite),
                ToolTip = Loc.GetString(groupInfo.Tooltip)
            };
            i++;
        }

        // Iterate through the top level actionOptions and add them
        foreach (var action in topLevelActions)
        {
            models[i] = action;
            i++;
        }

        return models;
    }

    private void HandleMenuOptionClick(RCDPrototype proto)
    {
        // A predicted message cannot be used here as the RCD UI is closed immediately
        // after this message is sent, which will stop the server from receiving it
        SendMessage(new RCDSystemMessage(proto.ID));


        if (_playerManager.LocalSession?.AttachedEntity == null)
            return;

        var msg = Loc.GetString("rcd-component-change-mode", ("mode", Loc.GetString(proto.SetName)));

        if (proto.Mode is RcdMode.ConstructTile or RcdMode.ConstructObject)
        {
            var name = Loc.GetString(proto.SetName);

            if (proto.Prototype != null &&
                _prototypeManager.TryIndex(proto.Prototype, out var entProto)) // don't use Resolve because this can be a tile
            {
                name = entProto.Name;
            }

            msg = Loc.GetString("rcd-component-change-build-mode", ("name", name));
        }

        // Popup message
        var popup = EntMan.System<PopupSystem>();
        popup.PopupClient(msg, Owner, _playerManager.LocalSession.AttachedEntity);
    }

    private string GetTooltip(RCDPrototype proto)
    {
        string tooltip;

        if (proto.Mode is RcdMode.ConstructTile or RcdMode.ConstructObject
            && proto.Prototype != null
            && _prototypeManager.TryIndex(proto.Prototype, out var entProto)) // don't use Resolve because this can be a tile
        {
            tooltip = Loc.GetString(entProto.Name);
        }
        else
        {
            tooltip = Loc.GetString(proto.SetName);
        }

        tooltip = OopsConcat(char.ToUpper(tooltip[0]).ToString(), tooltip.Remove(0, 1));

        return tooltip;
    }

    private static string OopsConcat(string a, string b)
    {
        // This exists to prevent Roslyn being clever and compiling something that fails sandbox checks.
        return a + b;
    }
}
