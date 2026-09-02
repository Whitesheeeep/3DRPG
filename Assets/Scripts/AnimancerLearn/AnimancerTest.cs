using System;
using System.Collections;
using System.Collections.Generic;
using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using WS_Modules.Utilities;

public class AnimancerTest : MonoBehaviour
{
    public List<AnimationClip> clips;

    private AnimancerComponent animancer;

    private void Awake()
    {
        animancer = GetComponent<AnimancerComponent>();

    }

    [Button("测试 DebugUtility")]
    private void TestDebugUtility()
    {
        DebugUtility.DrawCube(
            center: transform.position,
            size: new Vector3(2f, 1f, 3f),
            rotation: transform.rotation,
            color: Color.green,
            duration: 2f,
            depthTest: false);

        DebugUtility.DrawSphere(
            center: transform.position,
            radius: 1.5f,
            color: Color.yellow,
            duration: 1f);

        DebugUtility.DrawCapsule(
            center: transform.position,
            radius: 0.5f,
            height: 2.5f,
            rotation: transform.rotation,
            color: Color.cyan,
            duration: 1f);

        DebugUtility.DrawSector(
            center: transform.position,
            innerRadius: 0f,
            outerRadius: 5f,
            angle: 90f,
            height: 2f,
            rotation: transform.rotation,
            color: Color.red,
            duration: 1f,
            depthTest: true,
            segments: 32);
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            PlayClips();

        }
    }

    private void PlayClips()
    {
        PlayClips(0);
    }

    private void PlayClips(int startIndex)
    {
        var state = animancer.Play(clips[startIndex]);
        state.Events(this).OnEnd = () =>
        {
            int nextIndex = (startIndex + 1) % clips.Count;
            PlayClips(nextIndex);
        };
    }
}
