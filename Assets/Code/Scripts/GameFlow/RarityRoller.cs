using System.Collections.Generic;
using UnityEngine;

public static class RarityRoller
{
    public static Rarity Roll(IReadOnlyList<RarityWeight> weights, Rarity fallback = Rarity.Common)
    {
        if (weights == null || weights.Count == 0)
        {
            return fallback;
        }

        float totalWeight = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            totalWeight += Mathf.Max(0f, weights[i].weight);
        }

        if (totalWeight <= 0f)
        {
            return fallback;
        }

        float randomRoll = Random.Range(0f, totalWeight);
        float currentWeightSum = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            currentWeightSum += Mathf.Max(0f, weights[i].weight);
            if (randomRoll <= currentWeightSum)
            {
                return weights[i].rarity;
            }
        }

        return fallback;
    }
}
