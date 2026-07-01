using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UpgradeButtonUI : MonoBehaviour
{
    [Header("Visuals")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public Image iconImage;
    public Image frameImage;

    [Header("Interaction")]
    public Button selectButton;
    public Button rerollButton;
    public TextMeshProUGUI rerollCostText;

    private UpgradeData _currentTemplate;
    private Rarity _currentRarity;
    private PlayerStatsManager _statsManager;
    private UpgradeManager _manager;
    private System.Action _onSelectedCallback;
    private float _finalValue;

    public void Setup(UpgradeData template, Rarity rarity, PlayerStatsManager stats, UpgradeManager manager, System.Action onSelected)
    {
        _currentTemplate = template;
        _currentRarity = rarity;
        _statsManager = stats;
        _manager = manager;
        _onSelectedCallback = onSelected;

        RefreshVisuals();

        selectButton.onClick.RemoveAllListeners();
        selectButton.onClick.AddListener(OnSelectClick);

        if (rerollButton != null)
        {
            rerollButton.onClick.RemoveAllListeners();
            rerollButton.onClick.AddListener(OnRerollClick);
        }
    }

    public void RefreshVisuals()
    {
        Color rarityColor = _manager.GetRarityColor(_currentRarity);
        bool isLevelUpgrade = _currentTemplate.IsFirePointLevelUpgrade;

        if (isLevelUpgrade)
        {
            _finalValue = Mathf.Max(1, Mathf.RoundToInt(_currentTemplate.baseValue));
        }
        else
        {
            float multiplier = _manager.GetRarityMultiplier(_currentRarity);
            _finalValue = _currentTemplate.baseValue * multiplier;
        }

        titleText.text = _currentTemplate.upgradeName;
        titleText.color = rarityColor;

        if (frameImage != null) frameImage.color = rarityColor;

        Stat targetStat = (_currentTemplate.category == StatCategory.Base)
            ? _statsManager.GetHullStat(_currentTemplate.hullStatType)
            : _statsManager.GetWeaponStat(_currentTemplate.weaponStatType, _currentTemplate.category);

        string bonusText = BuildBonusText(isLevelUpgrade);

        if (targetStat != null)
        {
            descriptionText.text = $"{_currentTemplate.description}\n" +
                                   $"{BuildTransitionText(targetStat, isLevelUpgrade)} " +
                                   $"<color={ColorToHex(rarityColor)}>({bonusText})</color>";
        }
        else
        {
            descriptionText.text = $"{_currentTemplate.description}\n" +
                                   $"<color={ColorToHex(rarityColor)}>({bonusText})</color>";
        }

        if (iconImage != null) iconImage.sprite = _currentTemplate.icon;

        if (rerollCostText != null)
            rerollCostText.text = $"Reroll ({_manager.rerollsLeft})";

        if (rerollButton != null)
            rerollButton.interactable = _manager.rerollsLeft > 0;
    }

    private string BuildBonusText(bool isLevelUpgrade)
    {
        if (isLevelUpgrade) return $"+{(int)_finalValue} Level";
        string sign = _finalValue >= 0f ? "+" : "";
        return _currentTemplate.modType == StatModType.PercentAdd
            ? $"{sign}{_finalValue * 100:0}%"
            : $"{sign}{_finalValue:0}";
    }

    private string BuildTransitionText(Stat stat, bool isLevelUpgrade)
    {
        if (isLevelUpgrade)
        {
            int maxLevel = _statsManager.GetMaxFirePointLevel(_currentTemplate.category);
            int currentLevel = Mathf.Clamp(Mathf.FloorToInt(stat.Value), 1, maxLevel);
            int requested = currentLevel + (int)_finalValue;
            int newLevel = Mathf.Min(requested, maxLevel);

            string newLevelText = newLevel < requested || currentLevel >= maxLevel
                ? $"<color=#50C878>Level {newLevel}</color> <color=#FFB347>(MAX)</color>"
                : $"<color=#50C878>Level {newLevel}</color>";

            return $"Level {currentLevel} <color=white>→</color> {newLevelText}";
        }

        float newVal = CalculatePreview(stat);
        return $"{stat.Value:F1} <color=white>→</color> <color=#50C878>{newVal:F1}</color>";
    }

    private float CalculatePreview(Stat stat)
    {
        return _currentTemplate.modType == StatModType.Flat
            ? stat.Value + _finalValue
            : stat.Value * (1 + _finalValue);
    }

    private void OnSelectClick()
    {
        if (_currentTemplate.category == StatCategory.Base)
        {
            _statsManager.SetHullModifier(_currentTemplate.hullStatType, _finalValue, _currentTemplate.modType);
        }
        else if (_currentTemplate.IsFirePointLevelUpgrade)
        {
            ApplyFirePointLevelUpgrade();
        }
        else
        {
            _statsManager.SetWeaponModifier(_currentTemplate.category, _currentTemplate.weaponStatType, _finalValue, _currentTemplate.modType);
        }

        _onSelectedCallback?.Invoke();
    }

    private void ApplyFirePointLevelUpgrade()
    {
        Stat stat = _statsManager.GetWeaponStat(WeaponStatType.FirePointLevel, _currentTemplate.category);
        if (stat == null) return;

        int maxLevel = _statsManager.GetMaxFirePointLevel(_currentTemplate.category);
        int currentLevel = Mathf.Clamp(Mathf.FloorToInt(stat.Value), 1, maxLevel);
        int realDelta = Mathf.Max(0, maxLevel - currentLevel);
        if (realDelta <= 0) return;

        int requested = (int)_finalValue;
        int applied = Mathf.Min(realDelta, requested);
        if (applied <= 0) return;

        _statsManager.SetWeaponModifier(_currentTemplate.category, WeaponStatType.FirePointLevel, applied, StatModType.Flat);
    }

    private void OnRerollClick() => _manager.TryRerollSingle(this);

    public void UpdateData(UpgradeData newTemplate, Rarity newRarity)
    {
        _currentTemplate = newTemplate;
        _currentRarity = newRarity;
        RefreshVisuals();
    }

    private string ColorToHex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);
}
