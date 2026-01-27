using UnityEngine;

[CreateAssetMenu(fileName = "CharacterDatabase", menuName = "Game/Character Database")]
public class CharacterDatabase : ScriptableObject
{
    [SerializeField] private CharacterData[] characters;

    public int CharacterCount => characters?.Length ?? 0;

    public CharacterData GetCharacter(int index)
    {
        if (characters == null || index < 0 || index >= characters.Length)
            return null;
        return characters[index];
    }

    public CharacterData[] GetAllCharacters() => characters;

    public int GetCharacterIndex(CharacterData data)
    {
        if (characters == null || data == null) return -1;

        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] == data) return i;
        }
        return -1;
    }
}