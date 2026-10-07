using System;
using System.Linq;

using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Housing;

/// <summary>
/// Which items are housing fixtures and how the client sees them. Shared by <see cref="HousingZoneRuntime"/> and
/// <see cref="HousingCreativeCatalog"/>, so the creative catalog offers exactly what the editor can place.
/// </summary>
public static class HousingFixtureRules
{
    public static bool IsFixtureInventoryItem(IResourceManager resourceManager, int itemDefinitionId)
    {
        if (!resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var definition) ||
            definition.Type == 16)
        {
            return false;
        }

        if (HousingPlacementCatalog.IsFixtureCustomization(definition) || definition.Type == 29)
            return true;

        if (definition.Type != 1)
            return false;

        if (definition.CategoryId is 52 or 53 or 54 or 56 or 57 or 147)
            return HousingPlacementCatalog.IsFixture(itemDefinitionId) ||
                (!string.IsNullOrWhiteSpace(definition.ModelName) &&
                    definition.ModelName.StartsWith("hsg_", StringComparison.OrdinalIgnoreCase));

        return HousingPlacementCatalog.IsFixture(itemDefinitionId);
    }

    public static int ResolveItemTintId(IResourceManager resourceManager, int itemDefinitionId, int requestedTintId)
    {
        if (requestedTintId > 0)
            return requestedTintId;

        if (resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var definition) &&
            definition.CategoryId == 147 &&
            definition.Icon.TintId > 0)
        {
            return definition.Icon.TintId;
        }

        return 0;
    }

    public static FixtureDefinition? BuildFixtureDefinition(IResourceManager resourceManager, int itemDefinitionId)
    {
        if (!resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
            return null;

        var isCustomization = HousingPlacementCatalog.IsFixtureCustomization(itemDefinition);
        var hasPlacement = HousingPlacementCatalog.TryGet(itemDefinitionId, out var placement);
        var modelId = ResolveFixtureModelId(resourceManager, itemDefinitionId);
        var assetName = hasPlacement ? placement.AssetName : itemDefinition.ModelName ?? string.Empty;

        if (modelId == 0 &&
            !isCustomization &&
            !assetName.EndsWith(".agr", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new FixtureDefinition
        {
            Id = itemDefinitionId,
            ItemDefinitionId = itemDefinitionId,
            Unknown3 = isCustomization
                ? itemDefinition.Param1
                : hasPlacement ? placement.PlacementType : 1,
            ModelId = modelId,
            Category = itemDefinition.CategoryId.ToString(),
            LuaCall = string.Empty,
            Unknown7 = true,
            CompositeEffectId = itemDefinition.CompositeEffectId,
            Unknown14 = 1f,
            Unknown15 = 1f
        };
    }

    public static int ResolveFixtureModelId(IResourceManager resourceManager, int itemDefinitionId)
    {
        if (!resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var definition))
            return 0;

        if (!HousingPlacementCatalog.IsFixtureCustomization(definition) && definition.Param1 > 0)
            return definition.Param1;

        var modelName = HousingPlacementCatalog.TryGet(itemDefinitionId, out var placement)
            ? placement.AssetName
            : definition.ModelName;
        return ResolveModelId(resourceManager, modelName);
    }

    public static int ResolveModelId(IResourceManager resourceManager, string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return 0;

        return resourceManager.Models.Values
            .FirstOrDefault(model => string.Equals(
                model.ModelFileName,
                modelName,
                StringComparison.OrdinalIgnoreCase))
            ?.Id ?? 0;
    }
}
