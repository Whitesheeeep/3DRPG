using UnityEngine;
using WS_Modules;
using WS_Modules.AudioSystem;

public class EasyAudioPlay : MonoBehaviour
{
    [SerializeField, WSAddressableKey("AudioClips", "UI")] private string audioClipNameKey;

    public void PlayAudio()
    {
        if (audioClipNameKey != null)
        {
            AudioManager.Instance.PlaySFX(audioClipNameKey, Vector3.zero, autoRelease: false);
        }
    }
}
