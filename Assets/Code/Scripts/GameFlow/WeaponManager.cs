using UnityEngine;
using System.Collections.Generic;

public class WeaponManager : MonoBehaviour
{
    [Header("Pools & Config")]
    public List<WeaponData> allWeaponPool;
    public LevelRequirement startConfig; 

    [Header("UI & Player")]
    public Transform cardContainer;
    public GameObject weaponCardPrefab;
    public GameObject selectionPanel;
    public PlayerStatsManager playerStats;

    private readonly List<WeaponData> _currentChoices = new List<WeaponData>();
    public IReadOnlyList<WeaponData> CurrentChoices => _currentChoices;

    void Start()
    {
        var gsm = GameStateManager.Instance;
        bool shouldOpenSelection = gsm != null && gsm.CurrentState == GameState.ProjectileSelection;

        if (!shouldOpenSelection)
        {
            if (selectionPanel != null) selectionPanel.SetActive(false);
            SelectionScreenController.Instance?.HideWeaponSelection();
            return;
        }

        if (selectionPanel != null)
            selectionPanel.SetActive(!SelectionScreenController.IsAvailable);

        GenerateStartOptions();
    }

    private void GenerateStartOptions()
    {
        _currentChoices.Clear();

        if (cardContainer != null)
        {
            foreach (Transform child in cardContainer) Destroy(child.gameObject);
        }

        List<WeaponData> selectedThisTurn = new List<WeaponData>();
        bool usesToolkit = SelectionScreenController.IsAvailable;

        for (int i = 0; i < startConfig.upgradeOptionsCount; i++)
        {
            Rarity rolledRarity = RollRarity(startConfig.rarityWeights);
            WeaponData weapon = GetRandomWeaponOfRarity(rolledRarity, selectedThisTurn);

            if (weapon != null)
            {
                selectedThisTurn.Add(weapon);
                _currentChoices.Add(weapon);

                if (!usesToolkit && weaponCardPrefab != null && cardContainer != null)
                {
                    GameObject card = Instantiate(weaponCardPrefab, cardContainer);
                    card.GetComponent<WeaponCardUI>().Setup(weapon, this);
                }
            }
        }

        if (usesToolkit)
            SelectionScreenController.Instance.ShowWeaponSelection(this, _currentChoices);
    }

    public void ApplySelectedWeapon(WeaponData selectedWeapon)
    {
        if (playerStats != null && selectedWeapon != null)
        {
            if (selectedWeapon.isSpecialWeapon)
            {
                playerStats.ChangeSpecialWeapon(selectedWeapon);
            }
            else
            {
                playerStats.ChangeWeapon(selectedWeapon); 
            }
        }

        if (selectionPanel != null) selectionPanel.SetActive(false);
        SelectionScreenController.Instance?.HideWeaponSelection();
        GameStateManager.GetOrCreate().ChangeState(GameState.InGame);
    }

    private Rarity RollRarity(List<RarityWeight> weights)
    {
        return RarityRoller.Roll(weights);
    }

    private WeaponData GetRandomWeaponOfRarity(Rarity r, List<WeaponData> exclude)
    {
        WeaponData selected = SelectRandomWeapon(w => w != null && w.rarity == r && !exclude.Contains(w));
        if (selected != null)
        {
            return selected;
        }

        return SelectRandomWeapon(w => w != null && !exclude.Contains(w));
    }

    private WeaponData SelectRandomWeapon(System.Predicate<WeaponData> predicate)
    {
        if (allWeaponPool == null) return null;

        WeaponData selected = null;
        int validCount = 0;
        for (int i = 0; i < allWeaponPool.Count; i++)
        {
            WeaponData weapon = allWeaponPool[i];
            if (!predicate(weapon)) continue;

            validCount++;
            if (Random.Range(0, validCount) == 0)
            {
                selected = weapon;
            }
        }

        return selected;
    }
}
