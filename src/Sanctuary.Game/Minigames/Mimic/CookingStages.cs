using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.Game.Minigames.Mimic;

/// <summary>
/// What Cooking's Flash can draw: its round types (<c>Cooking.StageType</c>) and, for each, the ingredients
/// (<c>Cooking.CookingIngredient</c> ids, the phase's <c>LD_DATA1</c>) its scene has a frame for. Read from the frame
/// labels in <c>game_cooking.gfx</c>; a round or ingredient outside this table shows a blank or wrong scene.
/// </summary>
public static class CookingStages
{
    public const int Tenderize = 1;
    public const int Chop = 2;
    public const int SmashFruit = 3;
    public const int Crack = 4;
    public const int Measure = 6;
    public const int Stir = 7;
    public const int Saw = 8;
    public const int Grate = 9;
    public const int Slice = 10;
    public const int Cook = 11;
    public const int Cut = 12;
    public const int SetHeat = 13;
    public const int AddIngredients = 17;

    // Ingredient ids that recipes check for.
    public const int Egg = 23;
    public const int MeatStew = 46;
    public const int FishSoup = 47;

    /// <summary>
    /// Stage type → the ingredient ids its scene draws. Pour (5), final round (14), grind (15) and stuff (16) have
    /// no scene in this Flash.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, int[]> Ingredients = new Dictionary<int, int[]>
    {
        // Meat saute, fish saute, sundae, omelet, meat stew, fish soup, spaghetti.
        [AddIngredients] = [42, 43, 44, 45, 46, 47, 54],
        // Mystery, bear, bat and spider meat.
        [Tenderize] = [8, 9, 10, 11],
        [Saw] = [8, 9, 10, 11],
        // Rainleaf, sunleaf, spiralmint, scragglevine, florenleaf.
        [Chop] = [24, 25, 26, 27, 28],
        // Starnut, punkin, stickfruit, briarmelon, snowberry, bumbleberry, gummyfruit, stainberry, tomato.
        [SmashFruit] = [34, 35, 36, 37, 38, 39, 40, 41, 56],
        [Crack] = [Egg],
        // Slugmud, rootbeer, the six broths, vanilla and chocolate ice, egg, spaghetti (its sauce).
        [Measure] = [19, 20, 48, 49, 50, 51, 52, 53, 21, 22, Egg, 54],
        [Stir] = [MeatStew, FishSoup, 54],
        // Chugaroot, chocolate, honeytwig.
        [Grate] = [31, 32, 33],
        // Tumtumnip, moonbean, choychoy, snowturnip, nettleshroom, frostradish, glowtruffle, onion.
        [Slice] = [12, 13, 14, 15, 16, 17, 18, 57],
        // Meat saute, fish saute, spaghetti.
        [Cook] = [42, 43, 54],
        // The eight fish.
        [Cut] = [0, 1, 2, 3, 4, 5, 6, 7],
        // Vanilla and chocolate ice.
        [SetHeat] = [21, 22]
    };

    /// <summary>Rounds that put their ingredient on the recipe's list for the add-ingredients and cook rounds.</summary>
    private static readonly HashSet<int> GathersIngredient = [Tenderize, Chop, SmashFruit, Grate, Slice, Cut];

    public static IEnumerable<string> Problems(MimicPhaseDefinition phase)
    {
        if (!Ingredients.TryGetValue(phase.StageType, out var ingredients))
        {
            yield return $"Cooking has no scene for stage type {phase.StageType}";
            yield break;
        }

        var ingredient = phase.Data.Count > 0 ? phase.Data[0] : 0;
        if (!ingredients.Contains(ingredient))
            yield return $"stage type {phase.StageType} has no picture for ingredient {ingredient}";
    }

    /// <summary>Checks across a recipe's phases: rounds that use what earlier rounds made.</summary>
    public static IEnumerable<string> RecipeProblems(MimicRecipeDefinition recipe)
    {
        for (var i = 0; i < recipe.Phases.Count; i++)
        {
            var phase = recipe.Phases[i];
            var earlier = recipe.Phases.Take(i).ToList();
            var ingredient = phase.Data.Count > 0 ? phase.Data[0] : 0;

            // The list to add or the pan to fill is built from the rounds before.
            if (phase.StageType is AddIngredients or Cook && !earlier.Any(p => GathersIngredient.Contains(p.StageType)))
                yield return $"phase {i + 1}: nothing to add or cook; an earlier round must gather an ingredient";

            // The pot's broth is the liquid an earlier measure round poured.
            var needsBroth = phase.StageType == Stir && ingredient != 54 ||
                             phase.StageType == AddIngredients && ingredient is MeatStew or FishSoup;
            if (needsBroth && !earlier.Any(p => p.StageType == Measure))
                yield return $"phase {i + 1}: the broth comes from an earlier measure round, and there is none";
        }
    }
}
