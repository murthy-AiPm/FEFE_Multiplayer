using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

public class VCam : MonoBehaviour
{
    [SerializeField] CinemachineFreeLook vCam;
    public VCameraObject vCO,vZO;
    [SerializeField] Transform LookAt,Follow,ZoomLook;


    void Start()
    {
        
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
