using System.Collections.Generic;
using UnityEngine;

public sealed class UpgradeChoice
{
    public UpgradeData Template { get; }
    public Rarity Rarity { get; }

    public UpgradeChoice(UpgradeData template, Rarity rarity)
    {
        Template = template;
        Rarity = rarity;
    }
}

public class UpgradeManager : MonoBehaviour
{
    [Header("Pools & Config")]
    public List<UpgradeData> allUpgradeTemplates;
    public List<LevelRequirement> levelConfigs;

    [Header("UI References")]
    public GameObject upgradePanel;
    public Transform buttonsContainer;
    public UpgradeButtonUI buttonPrefab;
    public PlayerStatsManager playerStats;

    [Header("Economy")]
    public int rerollsLeft = 3;
    private int _currentLvl;

    private readonly Queue<int> _pendingLevels = new Queue<int>();
    private readonly List<UpgradeChoice> _currentChoices = new List<UpgradeChoice>();
    private bool _selectionActive;
    public IReadOnlyList<UpgradeChoice> CurrentChoices => _currentChoices;

    private void OnEnable() => ProgressionManager.OnLevelUp += QueueLevelUp;
    private void OnDisable() => ProgressionManager.OnLevelUp -= QueueLevelUp;

    private void QueueLevelUp(int currentLevel)
    {
        _pendingLevels.Enqueue(currentLevel);
        if (!_selectionActive) ProcessNext();
    }

    private void ProcessNext()
    {
        if (_pendingLevels.Count == 0)
        {
            _selectionActive = false;
            if (upgradePanel != null) upgradePanel.SetActive(false);
            SelectionScreenController.Instance?.HideUpgradeSelection();
            GameStateManager.GetOrCreate().ChangeState(GameState.InGame);
            return;
        }

        _selectionActive = true;
        _currentLvl = _pendingLevels.Dequeue();
        GameStateManager.GetOrCreate().ChangeState(GameState.UpgradeSelection);

        GenerateUpgradeCards();
        if (_currentChoices.Count == 0)
        {
            ProcessNext();
            return;
        }

        bool usesToolkit = SelectionScreenController.IsAvailable;
        if (upgradePanel != null)
            upgradePanel.SetActive(!usesToolkit);

        if (usesToolkit)
            SelectionScreenController.Instance.ShowUpgradeSelection(this, _currentLvl, _currentChoices);
    }

    private void GenerateUpgradeCards()
    {
        ClearLegacyButtons();
        _currentChoices.Clear();

        LevelRequirement config = GetCurrentLevelConfig();

        if (config.rarityWeights == null || config.rarityWeights.Count == 0)
        {
            return;
        }

        var usedTemplates = new HashSet<UpgradeData>();
        int desiredCount = Mathf.Max(0, config.upgradeOptionsCount);
        int maxAttempts = Mathf.Max(16, desiredCount * 8);
        int attempts = 0;

        while (_currentChoices.Count < desiredCount && attempts < maxAttempts)
        {
            attempts++;
            if (TryCreateChoice(config, usedTemplates, out UpgradeChoice choice))
            {
                _currentChoices.Add(choice);
                usedTemplates.Add(choice.Template);
            }
        }

        if (SelectionScreenController.IsAvailable)
            return;

        BuildLegacyButtons();
    }

    private void ClearLegacyButtons()
    {
        if (buttonsContainer == null) return;

        for (int i = buttonsContainer.childCount - 1; i >= 0; i--)
        {
            GameObject child = buttonsContainer.GetChild(i).gameObject;
            if (child.scene.name != null) Destroy(child);
        }
    }

    private void BuildLegacyButtons()
    {
        if (buttonsContainer == null || buttonPrefab == null) return;

        for (int i = 0; i < _currentChoices.Count; i++)
        {
            UpgradeChoice choice = _currentChoices[i];
            if (choice?.Template == null) continue;

            var btn = Instantiate(buttonPrefab, buttonsContainer);
            btn.Setup(choice.Template, choice.Rarity, playerStats, this, FinishSelection);
        }
    }

    private bool TryCreateChoice(LevelRequirement config, out UpgradeChoice choice)
    {
        return TryCreateChoice(config, null, out choice);
    }

    private bool TryCreateChoice(LevelRequirement config, IReadOnlyCollection<UpgradeData> excludedTemplates, out UpgradeChoice choice)
    {
        choice = null;
        if (config.rarityWeights == null || config.rarityWeights.Count == 0) return false;

        Rarity rolledRarity = RollRarity(config.rarityWeights);
        UpgradeData template = GetRandomTemplateForRarity(rolledRarity, excludedTemplates);

        if (template == null) return false;

        choice = new UpgradeChoice(template, rolledRarity);
        return true;
    }

    private Rarity RollRarity(List<RarityWeight> weights)
    {
        return RarityRoller.Roll(weights);
    }

    public bool TryRerollSingle(UpgradeButtonUI button)
    {
        if (rerollsLeft <= 0) return false;

        LevelRequirement config = GetCurrentLevelConfig();
        if (config.rarityWeights == null) return false;

        if (!TryCreateChoice(config, out UpgradeChoice choice)) return false;

        rerollsLeft--;
        button.UpdateData(choice.Template, choice.Rarity);
        return true;
    }

    public bool TryRerollSingle(int choiceIndex, out UpgradeChoice choice)
    {
        choice = null;
        if (rerollsLeft <= 0) return false;
        if (choiceIndex < 0 || choiceIndex >= _currentChoices.Count) return false;

        LevelRequirement config = GetCurrentLevelConfig();
        HashSet<UpgradeData> excludedTemplates = new HashSet<UpgradeData>();
        for (int i = 0; i < _currentChoices.Count; i++)
        {
            if (i == choiceIndex) continue;
            UpgradeData template = _currentChoices[i]?.Template;
            if (template != null) excludedTemplates.Add(template);
        }

        if (!TryCreateChoice(config, excludedTemplates, out choice)) return false;

        rerollsLeft--;
        _currentChoices[choiceIndex] = choice;
        return true;
    }

    private LevelRequirement GetCurrentLevelConfig()
    {
        if (levelConfigs == null || levelConfigs.Count == 0)
        {
            return default;
        }

        LevelRequirement best = default;
        int bestInterval = 0;
        LevelRequirement firstValid = default;
        bool hasFirstValid = false;

        for (int i = 0; i < levelConfigs.Count; i++)
        {
            var cfg = levelConfigs[i];
            if (cfg.interval <= 0) continue;

            if (!hasFirstValid)
            {
                firstValid = cfg;
                hasFirstValid = true;
            }

            if (_currentLvl % cfg.interval == 0 && cfg.interval > bestInterval)
            {
                best = cfg;
                bestInterval = cfg.interval;
            }
        }

        if (bestInterval > 0) return best;
        if (hasFirstValid) return firstValid;

        return default;
    }

    public UpgradeData GetRandomTemplateForRarity(Rarity r)
    {
        return GetRandomTemplateForRarity(r, null);
    }

    private UpgradeData GetRandomTemplateForRarity(Rarity r, IReadOnlyCollection<UpgradeData> excludedTemplates)
    {
        if (allUpgradeTemplates == null) return null;

        UpgradeData selected = null;
        int validCount = 0;
        for (int i = 0; i < allUpgradeTemplates.Count; i++)
        {
            UpgradeData template = allUpgradeTemplates[i];
            if (template == null) continue;
            if (!template.allowedRarities.Contains(r)) continue;
            if (!IsUpgradeAvailable(template)) continue;
            if (IsExcluded(template, excludedTemplates)) continue;

            validCount++;
            if (Random.Range(0, validCount) == 0)
            {
                selected = template;
            }
        }

        return selected;
    }

    private static bool IsExcluded(UpgradeData template, IReadOnlyCollection<UpgradeData> excludedTemplates)
    {
        if (template == null || excludedTemplates == null)
        {
            return false;
        }

        foreach (UpgradeData excluded in excludedTemplates)
        {
            if (excluded == template)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsUpgradeAvailable(UpgradeData upgrade)
    {
        if (upgrade == null) return false;
        if (!upgrade.IsSupportedUpgradeStat()) return false;
        if (!upgrade.IsFirePointLevelUpgrade) return true;

        if (playerStats == null) return false;

        Stat stat = playerStats.GetWeaponStat(WeaponStatType.FirePointLevel, upgrade.category);
        if (stat == null) return false;

        int currentLevel = Mathf.Max(1, Mathf.FloorToInt(stat.Value));
        int maxLevel = playerStats.GetMaxFirePointLevel(upgrade.category);
        return currentLevel < maxLevel;
    }

    public float GetRarityMultiplier(Rarity r)
    {
        return r switch {
            Rarity.Common    => 1.0f,
            Rarity.Uncommon  => 1.3f,
            Rarity.Rare      => 1.7f,
            Rarity.Epic      => 2.5f,
            Rarity.Legendary => 4.5f,
            _ => 1f
        };
    }

    public Color GetRarityColor(Rarity r)
    {
        return r switch {
            Rarity.Common    => new Color(0.7f, 0.7f, 0.7f),
            Rarity.Uncommon  => new Color(0.2f, 0.8f, 0.2f),
            Rarity.Rare      => new Color(0.2f, 0.6f, 1.0f),
            Rarity.Epic      => new Color(0.7f, 0.2f, 1.0f),
            Rarity.Legendary => new Color(1.0f, 0.8f, 0.0f),
            _ => Color.white
        };
    }

    public string BuildUpgradeDescription(UpgradeChoice choice)
    {
        if (choice?.Template == null) return "";

        UpgradeData template = choice.Template;
        Color rarityColor = GetRarityColor(choice.Rarity);
        bool isLevelUpgrade = template.IsFirePointLevelUpgrade;
        Stat targetStat = ResolveTargetStat(template);
        string bonusText = BuildBonusText(template, choice.Rarity);

        if (targetStat != null)
        {
            return $"{template.description}\n" +
                   $"{BuildTransitionText(template, choice.Rarity, targetStat, isLevelUpgrade)} " +
                   $"<color={ColorToHex(rarityColor)}>({bonusText})</color>";
        }

        return $"{template.description}\n<color={ColorToHex(rarityColor)}>({bonusText})</color>";
    }

    public string BuildUpgradeTargetLabel(UpgradeChoice choice)
    {
        if (choice?.Template == null) return "UNKNOWN";

        UpgradeData template = choice.Template;
        if (template.category == StatCategory.Base)
            return FormatEnumLabel(template.hullStatType.ToString());

        return $"{FormatEnumLabel(template.category.ToString())} · {FormatEnumLabel(template.weaponStatType.ToString())}";
    }

    public float CalculateFinalValue(UpgradeData template, Rarity rarity)
    {
        if (template == null) return 0f;
        if (template.IsFirePointLevelUpgrade)
            return Mathf.Max(1, Mathf.RoundToInt(template.baseValue));

        return template.baseValue * GetRarityMultiplier(rarity);
    }

    public void ApplyChoice(int choiceIndex)
    {
        if (choiceIndex < 0 || choiceIndex >= _currentChoices.Count) return;
        ApplyChoice(_currentChoices[choiceIndex]);
    }

    public void ApplyChoice(UpgradeChoice choice)
    {
        if (choice?.Template == null) return;
        if (playerStats == null)
        {
            FinishSelection();
            return;
        }

        UpgradeData template = choice.Template;
        float finalValue = CalculateFinalValue(template, choice.Rarity);

        if (template.category == StatCategory.Base)
        {
            playerStats.SetHullModifier(template.hullStatType, finalValue, template.modType);
        }
        else if (template.IsFirePointLevelUpgrade)
        {
            ApplyFirePointLevelUpgrade(template, finalValue);
        }
        else
        {
            playerStats.SetWeaponModifier(template.category, template.weaponStatType, finalValue, template.modType);
        }

        FinishSelection();
    }

    private Stat ResolveTargetStat(UpgradeData template)
    {
        if (template == null || playerStats == null) return null;

        return template.category == StatCategory.Base
            ? playerStats.GetHullStat(template.hullStatType)
            : playerStats.GetWeaponStat(template.weaponStatType, template.category);
    }

    private string BuildBonusText(UpgradeData template, Rarity rarity)
    {
        float finalValue = CalculateFinalValue(template, rarity);
        if (template.IsFirePointLevelUpgrade) return $"+{(int)finalValue} Level";
        string sign = finalValue >= 0f ? "+" : "";
        return template.modType == StatModType.PercentAdd
            ? $"{sign}{finalValue * 100:0}%"
            : $"{sign}{finalValue:0}";
    }

    private string BuildTransitionText(UpgradeData template, Rarity rarity, Stat stat, bool isLevelUpgrade)
    {
        if (isLevelUpgrade)
        {
            int maxLevel = playerStats.GetMaxFirePointLevel(template.category);
            int currentLevel = Mathf.Clamp(Mathf.FloorToInt(stat.Value), 1, maxLevel);
            int requested = currentLevel + (int)CalculateFinalValue(template, rarity);
            int newLevel = Mathf.Min(requested, maxLevel);

            string newLevelText = newLevel < requested || currentLevel >= maxLevel
                ? $"<color=#50C878>Level {newLevel}</color> <color=#FFB347>(MAX)</color>"
                : $"<color=#50C878>Level {newLevel}</color>";

            return $"Level {currentLevel} <color=white>→</color> {newLevelText}";
        }

        float finalValue = CalculateFinalValue(template, rarity);
        float newValue = template.modType == StatModType.Flat
            ? stat.Value + finalValue
            : stat.Value * (1 + finalValue);

        return $"{stat.Value:F1} <color=white>→</color> <color=#50C878>{newValue:F1}</color>";
    }

    private void ApplyFirePointLevelUpgrade(UpgradeData template, float finalValue)
    {
        if (playerStats == null) return;

        Stat stat = playerStats.GetWeaponStat(WeaponStatType.FirePointLevel, template.category);
        if (stat == null) return;

        int maxLevel = playerStats.GetMaxFirePointLevel(template.category);
        int currentLevel = Mathf.Clamp(Mathf.FloorToInt(stat.Value), 1, maxLevel);
        int realDelta = Mathf.Max(0, maxLevel - currentLevel);
        if (realDelta <= 0) return;

        int requested = (int)finalValue;
        int applied = Mathf.Min(realDelta, requested);
        if (applied <= 0) return;

        playerStats.SetWeaponModifier(template.category, WeaponStatType.FirePointLevel, applied, StatModType.Flat);
    }

    private static string ColorToHex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

    private static string FormatEnumLabel(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        var result = new System.Text.StringBuilder(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(value[i - 1]))
                result.Append(' ');
            result.Append(c);
        }

        return result.ToString().ToUpperInvariant();
    }

    private void FinishSelection() => ProcessNext();
}
