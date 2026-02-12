using UnityEngine;

[CreateAssetMenu(fileName = "CharacterData", menuName = "Game/Character Data")]
public class CharacterData : ScriptableObject
{
    [Header("Identity")]
    public string characterName;
    public Sprite icon;
    public Color characterColor = Color.white;

    [Header("Prefab")]
    public GameObject prefab;

    [Header("Combat")]
    [Tooltip("Weapon loadout for this character class. Defines slot 1, slot 2, and shield.")]
    public CombatLoadout combatLoadout;
}