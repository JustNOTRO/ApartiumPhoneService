using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorceryMedia.SDL2;
using static SDL2.SDL;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ApartiumPhoneService;

public class VoIpAudioPlayer
{
    //const String WAV_FILE_PATH = "./../../../../../media/file_example_WAV_1MG.wav";
    private IntPtr _audioBuffer; /* Pointer to wave data - uint8 */
    private uint _audioLen; /* Length of wave data * - uint32 */
    private int _audioPos; /* Current play position */

    private SDL_AudioSpec _audioSpec;
    private uint _deviceId; // SDL Device Id

    private bool EndAudioFile { get; set; }
    
    private readonly ILogger _logger;
    
    public VoIpAudioPlayer()
    {
        _logger = InitLogger();
        SDL2Helper.InitSDL();
        
        _logger.LogInformation("Initialized VoIpAudioPlayer");

        // Get list of Audio Playback devices
        var sdlDevices = SDL2Helper.GetAudioPlaybackDevices();

        // Quit since no Audio playback found
        if (sdlDevices.Count == 0)
        {
            _logger.LogError("No Audio playback devices found ...");
            SDL2Helper.QuitSDL();
        }
    }
    
    /// <summary>
    /// Initializes the sip request handler logger
    /// </summary>
    /// <returns>the server logger</returns>
    private ILogger InitLogger()
    {
        var serilogLogger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
            .WriteTo.Console()
            .CreateLogger();

        var factory = new SerilogLoggerFactory(serilogLogger);
        SIPSorcery.LogFactory.Set(factory);
        return factory.CreateLogger<SIPRequestHandler>();
    }

    public virtual void Play(VoIpSound sound)
    {
        EndAudioFile = false;
        var destination = sound.GetDestination();
        
        // Open WAV file:
        if (SDL_LoadWAV(destination, out _audioSpec, out _audioBuffer, out _audioLen) < 0)
        {
            _logger.LogError("\nCannot open audio file - its format is not supported");
            SDL2Helper.QuitSDL();
            Environment.Exit(1);
        }

        // Check len of the Wav file
        if (_audioLen == 0)
        {
            _logger.LogError("\nAudio file not found - path is incorrect");
            SDL2Helper.QuitSDL();
            Environment.Exit(1);
        }

        // Set callback used to play audio (we fill the device using bytes)
        _audioSpec.callback = FillWavData;

        // Open audio file and start to play Wav file
        _deviceId = OpenAudioDevice();

        if (_deviceId == 0)
        {
            _logger.LogError("\nCannot open Audio device ...");
            SDL2Helper.QuitSDL();
            Environment.Exit(1);
        }
        
        _logger.LogInformation("Playing file: {0}", destination);

        SDL_FlushEvents(SDL_EventType.SDL_AUDIODEVICEADDED, SDL_EventType.SDL_AUDIODEVICEREMOVED);

        while (!EndAudioFile)
        {
            SDL_Delay(100);
        }
        
        _logger.LogInformation("Finished playing {0}", destination);
        SDL_FreeWAV(_audioBuffer);
        
        // No more need callback
        _audioSpec.callback = null;
        
        CloseAudioDevice();
    }

    public void Stop()
    {
        ToggleAudioPlay(false);
    }
    
    public bool IsCurrentlyPlaying => EndAudioFile;

    private void CloseAudioDevice()
    {
        if (_deviceId != 0)
            SDL_CloseAudioDevice(_deviceId);
    }

    private void ToggleAudioPlay(bool state)
    {
        if (_deviceId != 0)
        {
            /* Let the audio run */
            SDL_PauseAudioDevice(_deviceId, !state ? SDL_TRUE : SDL_FALSE);
        }
    }
    
    private uint OpenAudioDevice()
    {
        /* Initialize fillerup() variables */
        _deviceId = SDL_OpenAudioDevice(null, SDL_FALSE, ref _audioSpec, out _,
            0);
        
        ToggleAudioPlay(true);

        return _deviceId;
    }

    private void FillWavData(IntPtr unused, IntPtr stream, int len)
    {
        if (EndAudioFile)
            return;

        /* Set up the pointers */
        var wavePtr = _audioBuffer + _audioPos; // Uint8
        var waveLeft = (int)(_audioLen - _audioPos);

        if (waveLeft <= len)
        {
            SDL_memcpy(stream, wavePtr, new IntPtr(waveLeft));
            _audioPos = 0;
            EndAudioFile = true;
        }
        else
        {
            SDL_memcpy(stream, wavePtr, new IntPtr(len));
            _audioPos += len;
        }
    }
}