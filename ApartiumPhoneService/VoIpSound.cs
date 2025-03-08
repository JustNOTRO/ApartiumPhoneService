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
    
    public static readonly VoIpSound WelcomeSound = new("Sounds/welcome-sound.wav", 5);
    public static readonly VoIpSound ZeroSound = new("Sounds/zero.wav", 2);
    public static readonly VoIpSound OneSound = new("Sounds/one.wav", 2);
    public static readonly VoIpSound TwoSound = new("Sounds/two.wav", 2);
    public static readonly VoIpSound ThreeSound = new("Sounds/three.wav", 2);
    public static readonly VoIpSound FourSound = new("Sounds/four.wav", 2);
    public static readonly VoIpSound FiveSound = new("Sounds/five.wav", 2);
    public static readonly VoIpSound SixSound = new("Sounds/six.wav", 2);
    public static readonly VoIpSound SevenSound = new("Sounds/seven.wav", 2);
    public static readonly VoIpSound EightSound = new("Sounds/eight.wav", 2);
    public static readonly VoIpSound NineSound = new("Sounds/nine.wav", 2);
}