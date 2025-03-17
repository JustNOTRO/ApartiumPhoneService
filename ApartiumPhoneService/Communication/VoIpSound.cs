namespace ApartiumPhoneService;

public class VoIpSound(string destination, int duration)
{
    public string GetDestination()
    {
        return destination;
    }

    public int GetDuration()
    {
        return duration;
    }
    
    public static readonly VoIpSound WelcomeSound = new("Sounds/welcome.wav", 5);
    public static readonly VoIpSound ExplanationSound = new("Sounds/explanation.wav", 10);
    public static readonly VoIpSound NumbersNotFound = new("Sounds/numbers-not-found.wav", 5);
    private static readonly VoIpSound ZeroSound = new("Sounds/zero.wav", 5);
    private static readonly VoIpSound OneSound = new("Sounds/one.wav", 5);
    private static readonly VoIpSound TwoSound = new("Sounds/two.wav", 5);
    private static readonly VoIpSound ThreeSound = new("Sounds/three.wav", 5);
    private static readonly VoIpSound FourSound = new("Sounds/four.wav", 5);
    private static readonly VoIpSound FiveSound = new("Sounds/five.wav", 5);
    private static readonly VoIpSound SixSound = new("Sounds/six.wav", 5);
    private static readonly VoIpSound SevenSound = new("Sounds/seven.wav", 5);
    private static readonly VoIpSound EightSound = new("Sounds/eight.wav", 5);
    private static readonly VoIpSound NineSound = new("Sounds/nine.wav", 5);

    public static VoIpSound[] Values()
    {
        return
        [
            WelcomeSound,
            ZeroSound,
            OneSound,
            TwoSound,
            ThreeSound,
            FourSound,
            FiveSound,
            SixSound,
            SevenSound,
            EightSound,
            NineSound
        ];
    }
    
    
}