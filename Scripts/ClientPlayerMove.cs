using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;
using Cinemachine;

public class ClientPlayerMove : NetworkBehaviour
{
    [SerializeField] private PlayerInput m_PlayerInput;
    [SerializeField] private StarterAssetsInputs m_StarterAssetsInputs;
    [SerializeField] private ThirdPersonController m_ThirdPersonController;
    [SerializeField] private CinemachineVirtualCamera vcam;   // <— assign in prefab

    private void Awake()
    {
        // Everything off by default
        m_PlayerInput.enabled = false;
        m_StarterAssetsInputs.enabled = false;
        m_ThirdPersonController.enabled = false;

        if (vcam != null)
        {
            vcam.gameObject.SetActive(false);     // or vcam.Priority = 0;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            // Local player on THIS machine
            m_PlayerInput.enabled = true;
            m_StarterAssetsInputs.enabled = true;

            // For client-side movement:
            m_ThirdPersonController.enabled = true;

            if (vcam != null)
            {
                vcam.gameObject.SetActive(true);
                vcam.Priority = 20;              // higher than any default vcams
            }
        }
        else
        {
            // Remote players on THIS machine
            m_PlayerInput.enabled = false;
            m_StarterAssetsInputs.enabled = false;
            m_ThirdPersonController.enabled = false;

            if (vcam != null)
            {
                vcam.gameObject.SetActive(false);
                vcam.Priority = 0;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && vcam != null)
        {
            vcam.gameObject.SetActive(false);
        }

        base.OnNetworkDespawn();
    }
}

    //[Rpc(target:SendTo.Server)]
    //private void UpdateInputServerRpc(Vector2 move, Vector2 look, bool jump, bool sprint)
    //{
    //    m_StarterAssetsInputs.MoveInput(move);
    //    m_StarterAssetsInputs.LookInput(look);
    //    m_StarterAssetsInputs.JumpInput(jump);
    //    m_StarterAssetsInputs.SprintInput(sprint);
    //}

    //private void LateUpdate()
    //{
    //    if (!IsOwner) { return; }
    //    UpdateInputServerRpc(m_StarterAssetsInputs.move, m_StarterAssetsInputs.look,
    //        m_StarterAssetsInputs.jump, m_StarterAssetsInputs.sprint);
    //}
//}
