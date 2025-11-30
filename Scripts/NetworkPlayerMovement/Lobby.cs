using Unity.Services.Core;
using UnityEngine;

public class Lobby : MonoBehaviour
{

    private async void Start()
    {
        await UnityServices.InitializeAsync();
    }

}
