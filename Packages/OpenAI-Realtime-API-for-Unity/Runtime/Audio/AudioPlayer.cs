using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioPlayer : MonoBehaviour
{
    private AudioSource audioSource;
    private bool isPlayingAudio = false;
    private bool cancelPending = false;
    public float[] aiFrequencyData { get; private set; }
    private List<float> audioBuffer = new List<float>();
    private AudioClip playbackClip;
    private const int BUFFER_SIZE = 48000;
    private const float MIN_BUFFER_TIME = 0.1f;
    public int sampleRate = 24000;
    public int fftSampleSize = 1024;

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
    }

    /// <summary>
    /// enqueues audio data for playback
    /// </summary>
    public void EnqueueAudioData(byte[] pcmAudioData)
    {
        if (cancelPending) return;
        float[] floatData = AudioProcessingUtils.ConvertPCM16ToFloat(pcmAudioData);
        audioBuffer.AddRange(floatData);
        if (!isPlayingAudio)
        {
            StartCoroutine(PlayAudioCoroutine());
        }
    }

    private IEnumerator PlayAudioCoroutine()
    {
        isPlayingAudio = true;
        while (isPlayingAudio)
        {
            if (audioBuffer.Count >= sampleRate * MIN_BUFFER_TIME)
            {
                int samplesToPlay = Mathf.Min(BUFFER_SIZE, audioBuffer.Count);
                float[] audioChunk = new float[samplesToPlay];
                audioBuffer.CopyTo(0, audioChunk, 0, samplesToPlay);
                audioBuffer.RemoveRange(0, samplesToPlay);
                playbackClip = AudioClip.Create("PlaybackClip", samplesToPlay, 1, sampleRate, false);
                playbackClip.SetData(audioChunk, 0);
                audioSource.clip = playbackClip;
                audioSource.Play();
                yield return new WaitForSeconds((float)samplesToPlay / sampleRate);
            }
            else if (audioBuffer.Count > 0)
            {
                float[] audioChunk = audioBuffer.ToArray();
                audioBuffer.Clear();
                playbackClip = AudioClip.Create("PlaybackClip", audioChunk.Length, 1, sampleRate, false);
                playbackClip.SetData(audioChunk, 0);
                audioSource.clip = playbackClip;
                audioSource.Play();
                yield return new WaitForSeconds((float)audioChunk.Length / sampleRate);
            }
            else if (audioBuffer.Count == 0 && !audioSource.isPlaying)
            {
                yield return new WaitForSeconds(0.1f);
                if (audioBuffer.Count == 0) isPlayingAudio = false;
            }
            else
            {
                yield return null;
            }
        }
        ClearAudioBuffer();
    }

    private void Update()
    {
        UpdateAIFrequencyData();
    }

    private void UpdateAIFrequencyData()
    {
        if (!audioSource.isPlaying)
        {
            aiFrequencyData = null;
            return;
        }
        int fftSize = fftSampleSize;
        aiFrequencyData = new float[fftSize];
        audioSource.GetSpectrumData(aiFrequencyData, 0, FFTWindow.BlackmanHarris);
    }

    /// <summary>
    /// cancels audio playback
    /// </summary>
    public void CancelAudioPlayback()
    {
        cancelPending = true;
        StopAllCoroutines();
        ClearAudioBuffer();
    }

    /// <summary>
    /// clears audio buffer
    /// </summary>
    public void ClearAudioBuffer()
    {
        audioBuffer.Clear();
        audioSource.Stop();
        isPlayingAudio = false;
        aiFrequencyData = null;
    }

    /// <summary>
    /// checks if audio is playing
    /// </summary>
    public bool IsAudioPlaying()
    {
        return audioSource.isPlaying || audioBuffer.Count > 0;
    }

    /// <summary>
    /// resets cancel pending flag
    /// </summary>
    public void ResetCancelPending()
    {
        cancelPending = false;
    }
}
