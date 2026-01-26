using UnityEngine;

[CreateAssetMenu(fileName = "CharacterData", menuName = "Game/Character Data")]
public class CharacterData : ScriptableObject
{
    public string characterName;
    public Sprite icon;
    public Color characterColor = Color.white;
    public GameObject prefab; // Reference to the network prefab
}
