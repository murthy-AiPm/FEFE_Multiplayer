using UnityEngine;

public class GetPlayerPosition : MonoBehaviour
{
    private Transform playerTransform;

    void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");

        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
        }
        else
        {
            Debug.LogWarning("No GameObject with tag 'Player' found!");
        }
    }

    void Update()
    {
        if (playerTransform != null)
        {
            Vector3 pos = playerTransform.position;
            Debug.Log("Player position: " + pos);
        }
    }
}
