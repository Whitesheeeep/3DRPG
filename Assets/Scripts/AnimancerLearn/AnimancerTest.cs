using System;
using System.Collections;
using System.Collections.Generic;
using Animancer;
using UnityEngine;
using UnityEngine.InputSystem;

public class AnimancerTest : MonoBehaviour
{
    public List<AnimationClip> clips;

    private AnimancerComponent animancer;

    private void Awake()
    {
        animancer = GetComponent<AnimancerComponent>();
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
