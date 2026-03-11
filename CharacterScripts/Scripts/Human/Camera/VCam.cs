using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

public class VCam : MonoBehaviour
{
    [SerializeField] CinemachineFreeLook vCam;
    public VCameraObject vCO,vZO;
    [SerializeField] Transform LookAt,Follow,ZoomLook;

    // Base axis speeds (at sensitivity multiplier = 1).
    // Adjust these to match the feel you want at the default 1x setting.
    [SerializeField] private float baseSensX = 300f;
    [SerializeField] private float baseSensY = 2f;

    private const string PrefSensX = "Sens_X";

    void Start() { }

    private void OnEnable()
    {
        // Apply saved sensitivity when this camera activates (owner-only objects
        // start disabled and are enabled after network spawn).
        ApplySensitivity(PlayerPrefs.GetFloat(PrefSensX, 1f));
        SettingsMenuUI.OnSensitivityChanged += ApplySensitivity;
    }

    private void OnDisable()
    {
        SettingsMenuUI.OnSensitivityChanged -= ApplySensitivity;
    }

    private void ApplySensitivity(float multiplier)
    {
        if (vCam == null) return;
        vCam.m_XAxis.m_MaxSpeed = baseSensX * multiplier;
        vCam.m_YAxis.m_MaxSpeed = baseSensY * multiplier;
    }

    void Update()
    {
        if(ZoomLook == null || vZO == null)
        {
            return;
        }

        if (Input.GetButton("SecondaryAttack"))
        {
          
            vCam.LookAt = ZoomLook;
            vCam.Follow = ZoomLook;
            vCam.m_Orbits[0].m_Height = vZO.TopRigHeight;
            vCam.m_Orbits[0].m_Radius = vZO.TopRigRadius;
            vCam.m_Orbits[1].m_Height = vZO.MiddleRigHeight;
            vCam.m_Orbits[1].m_Radius = vZO.MiddleRigRadius;
            vCam.m_Orbits[2].m_Height = vZO.BottomRigHeight;
            vCam.m_Orbits[2].m_Radius = vZO.BotttomRigRadius;
            vCam.m_Lens.FieldOfView =   vZO.VerticalFOV;
        }
        else
        {
            vCam.m_Orbits[0].m_Height = vCO.TopRigHeight;
            vCam.m_Orbits[0].m_Radius = vCO.TopRigRadius;
            vCam.m_Orbits[1].m_Height = vCO.MiddleRigHeight;
            vCam.m_Orbits[1].m_Radius = vCO.MiddleRigRadius;
            vCam.m_Orbits[2].m_Height = vCO.BottomRigHeight;
            vCam.m_Orbits[2].m_Radius = vCO.BotttomRigRadius;

            vCam.LookAt = LookAt;
            vCam.Follow = Follow;

            vCam.m_Lens.FieldOfView = vCO.VerticalFOV;
        }


    }


}
