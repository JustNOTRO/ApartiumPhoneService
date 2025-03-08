using SIPSorceryMedia.SDL2;
using static SDL2.SDL;

namespace ApartiumPhoneService;

public class VoIpAudioPlayer
{
    private const string WavFilePath = "Sounds/welcome-sound.wav";

    //const String WAV_FILE_PATH = "./../../../../../media/file_example_WAV_1MG.wav";
    private static IntPtr _audioBuffer; /* Pointer to wave data - uint8 */
    private static uint _audioLen; /* Length of wave data * - uint32 */
    private static int _audioPos; /* Current play position */

    private static SDL_AudioSpec _audioSpec;
    private static uint _deviceId; // SDL Device Id

    private static bool _endAudioFile;

    public VoIpAudioPlayer()
    {
        Console.Clear();
        Console.WriteLine("\nTry to init SDL2 libraries - they must be stored in the same folder than this application");

        // Init SDL Library - Library files must be in the same folder as the application
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

        int deviceIndex; // To store the index of the audio playback device selected
        
        // Allow end user to select Audio playback device
        while (true)
        {
            Console.WriteLine("\nSelect audio playback device:");
            int index = 1;
            foreach (String device in sdlDevices)
            {
                Console.Write($"\n [{index}] - {device} ");
                index++;
            }

            Console.WriteLine("\n");
            Console.Out.Flush();

            var keyConsole = Console.ReadKey();
            if (!int.TryParse("" + keyConsole.KeyChar, out var keyValue) || keyValue >= index || keyValue < 0)
            {
                continue;
            }
            
            deviceIndex = keyValue - 1;
            break;
        }

        // Get name of the device
        var deviceName = sdlDevices[deviceIndex]; // To store the name of the audio playback device selected
        Console.WriteLine($"\nDevice selected: {deviceName}");

        // Open WAV file:
        if (SDL_LoadWAV(WavFilePath, out _audioSpec, out _audioBuffer, out _audioLen) == null)
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
        _deviceId = OpenAudioDevice(deviceName);

        if (_deviceId == 0)
        {
            Console.WriteLine("\nCannot open Audio device ...");
            SDL2Helper.QuitSDL();
            Environment.Exit(1);
        }
    }

    public void Play()
    {
        Console.WriteLine($"\nPlaying file: {WavFilePath}");

        SDL_FlushEvents(SDL_EventType.SDL_AUDIODEVICEADDED, SDL_EventType.SDL_AUDIODEVICEREMOVED);
        while (!_endAudioFile)
        {
            while (SDL_PollEvent(out var sdlEvent1) > 0)
            {
                if (sdlEvent1.type == SDL_EventType.SDL_QUIT)
                {
                    _endAudioFile = true;
                }
            }

            SDL_Delay(100);
        }

        // No more need callback
        _audioSpec.callback = null;

        // Free WAV file
        SDL_FreeWAV(_audioBuffer);

        // Close audio file
        CloseAudioDevice(_deviceId);

        // Quit SDL Library
        SDL2Helper.QuitSDL();
    }

    static void CloseAudioDevice(uint deviceId)
    {
        if (deviceId != 0)
            SDL_CloseAudioDevice(deviceId);
    }

    static uint OpenAudioDevice(String deviceName)
    {
        /* Initialize fillerup() variables */
        _deviceId = SDL_OpenAudioDevice(deviceName, SDL_FALSE, ref _audioSpec, out _,
            0);
        if (_deviceId != 0)
        {
            /* Let the audio run */
            SDL_PauseAudioDevice(_deviceId, SDL_FALSE);
        }

        return _deviceId;
    }

    static void FillWavData(IntPtr unused, IntPtr stream, int len)
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