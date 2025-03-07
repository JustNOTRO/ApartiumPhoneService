namespace ApartiumPhoneService;

public class VoIpSound(string destination, int duration)
{
    public string Destination { get; }
    public int Duration { get; }
    
    public static readonly VoIpSound WelcomeSound = new("Sounds/welcome-sound.wav", 2);
    public static readonly VoIpSound ZeroSound = new("Sounds/zero-sound.wav", 2);
    public static readonly VoIpSound OneSound = new("Sounds/one-sound.wav", 2);
    public static readonly VoIpSound TwoSound = new("Sounds/two-sound.wav", 2);
    public static readonly VoIpSound ThreeSound = new("Sounds/three-sound.wav", 2);
    public static readonly VoIpSound FourSound = new("Sounds/four-sound.wav", 2);
    public static readonly VoIpSound FiveSound = new("Sounds/five-sound.wav", 2);
    public static readonly VoIpSound SixSound = new("Sounds/six-sound.wav", 2);
    public static readonly VoIpSound SevenSound = new("Sounds/seven-sound.wav", 2);
    public static readonly VoIpSound EightSound = new("Sounds/eight-sound.wav", 2);
    public static readonly VoIpSound NineSound = new("Sounds/nine-sound.wav", 2);
}