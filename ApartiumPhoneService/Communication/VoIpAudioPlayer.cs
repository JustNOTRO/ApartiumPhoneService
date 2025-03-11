using SIPSorcery.SIP.App;
using SIPSorceryMedia.SDL2;
using static SDL2.SDL;

namespace ApartiumPhoneService;

public class VoIpAudioPlayer
{
    //const String WAV_FILE_PATH = "./../../../../../media/file_example_WAV_1MG.wav";
    private IntPtr _audioBuffer; /* Pointer to wave data - uint8 */
    private uint _audioLen; /* Length of wave data * - uint32 */
    private int _audioPos; /* Current play position */

    private SDL_AudioSpec _audioSpec;
    private uint _deviceId; // SDL Device Id

    private bool _endAudioFile;
    
    public void setEndAudioFile(bool endAudioFile)
    {
        _endAudioFile = endAudioFile;
    }
    
    public VoIpAudioPlayer()
    {
        Console.WriteLine("Initialized VoIpAudioPlayer");
        
        SDL2Helper.InitSDL();

        Console.WriteLine("\nInit done");

        // Get list of Audio Playback devices
        var sdlDevices = SDL2Helper.GetAudioPlaybackDevices();

        // Quit since no Audio playback found
        if (sdlDevices.Count == 0)
        {
            Console.WriteLine("No Audio playback devices found ...");
            SDL2Helper.QuitSDL();
            Environment.Exit(1);
        }
    }

    public virtual void Play(VoIpSound sound)
    {
        _endAudioFile = false;
        var destination = sound.GetDestination();
        
        // Open WAV file:
        if (SDL_LoadWAV(destination, out _audioSpec, out _audioBuffer, out _audioLen) < 0)
        {
            Console.WriteLine("\nCannot open audio file - its format is not supported");
            SDL2Helper.QuitSDL();
            Environment.Exit(1);
        }

        // Check len of the Wav file
        if (_audioLen == 0)
        {
            Console.WriteLine("\nAudio file not found - path is incorrect");
            SDL2Helper.QuitSDL();
            Environment.Exit(1);
        }

        // Set callback used to play audio (we fill the device using bytes)
        _audioSpec.callback = FillWavData;

        // Open audio file and start to play Wav file
        _deviceId = OpenAudioDevice();

        if (_deviceId == 0)
        {
            Console.WriteLine("\nCannot open Audio device ...");
            SDL2Helper.QuitSDL();
            Environment.Exit(1);
        }
        
        Console.WriteLine($"\nPlaying file: {destination}");

        SDL_FlushEvents(SDL_EventType.SDL_AUDIODEVICEADDED, SDL_EventType.SDL_AUDIODEVICEREMOVED);
        while (!_endAudioFile)
        {
            var pollEvent = SDL_PollEvent(out var sdlEvent);
            if (pollEvent > 0 && sdlEvent.type == SDL_EventType.SDL_QUIT)
            {
                break;
            }
            
            SDL_Delay(100);
        }
        
        // No more need callback
        _audioSpec.callback = null;

        // Free WAV file
        SDL_FreeWAV(_audioBuffer);

        Console.WriteLine("Finished playing {0}", destination);
        
        CloseAudioDevice();
    }

    public void CloseAudioDevice()
    {
        if (_deviceId != 0)
            SDL_CloseAudioDevice(_deviceId);
    }

    private uint OpenAudioDevice()
    {
        /* Initialize fillerup() variables */
        _deviceId = SDL_OpenAudioDevice(null, SDL_FALSE, ref _audioSpec, out _,
            0);
        if (_deviceId != 0)
        {
            /* Let the audio run */
            SDL_PauseAudioDevice(_deviceId, SDL_FALSE);
        }

        return _deviceId;
    }

    private void FillWavData(IntPtr unused, IntPtr stream, int len)
    {
        if (_endAudioFile)
            return;

        /* Set up the pointers */
        var wavePtr = _audioBuffer + _audioPos; // Uint8
        var waveLeft = (int)(_audioLen - _audioPos);

        if (waveLeft <= len)
        {
            SDL_memcpy(stream, wavePtr, new IntPtr(waveLeft));
            _audioPos = 0;
            _endAudioFile = true;
        }
        else
        {
            SDL_memcpy(stream, wavePtr, new IntPtr(len));
            _audioPos += len;
        }
    }
}