using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace ApartiumPhoneService;

public class ApartiumPhoneServer(List<ApartiumPhoneServer.SipRegisterAccount> sipAccounts, SIPTransport sipTransport)
{
    private static readonly Microsoft.Extensions.Logging.ILogger Logger = InitLogger();

    public static Microsoft.Extensions.Logging.ILogger GetLogger()
    {
        return Logger;
    }

    private class Constants
    {
        public const int SipListenPort = 5060;
        public const int SipsListenPort = 5061;
        public const string SipsCertificatePath = "localhost.pfx";
        public const string LocalHostAddress = "127.0.0.1";
        public const string LocalHostIpV4Address = "127.0.0.1";
        public const string LocalHostIpV6Address = "::1";
        public const string LocalHostIpv4Cmd = "lv4";
        public const string LocalHostIpv6Cmd = "lv6";
        public const int ExitSuccess = 0;
        public const int ExitFailure = 1;
    }

    public struct SipRegisterAccount(string username, string password, string domain, int expiry)
    {
        public readonly string Username = username;
        public readonly string Password = password;
        public readonly string Domain = domain;
        public readonly int Expiry = expiry;
    }

    /// <summary>
    /// The set of SIP accounts available for registering and/or authenticating calls.
    /// </summary>
    private readonly List<SipRegisterAccount> _sipAccounts = sipAccounts;

    /// <summary>
    /// Keeps track of the current active calls. It includes both received and placed calls.
    /// </summary>
    private readonly ConcurrentDictionary<string, SIPUserAgent> _calls = new();

    /// <summary>
    /// Keeps track of the SIP account registrations.
    /// </summary>
    private ConcurrentDictionary<string, SIPRegistrationUserAgent> _registrations = new();

    public SIPTransport GetSipTransport()
    {
        return sipTransport;
    }
    
    private IPAddress address;

    public IPAddress GetAddress()
    {
        return address;
    }

    public void Start()
    {
        Console.WriteLine("-----Lazy commands-----");
        Console.WriteLine("{0} - equivalent to {1} (IPV4 localhost)", Constants.LocalHostIpv4Cmd, Constants.LocalHostIpV4Address);
        Console.WriteLine("{0} - equivalent to {1} (IPV6 localhost)", Constants.LocalHostIpv6Cmd, Constants.LocalHostIpV6Address);
        Console.WriteLine();
        
        Console.Write("Please enter IP Address: ");
        var ipAddress = Console.ReadLine();
        SetAddress(ipAddress);
        
        sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(address, Constants.SipListenPort)));

        // var localhostCertificate = new X509Certificate2(Constants.SipsCertificatePath);
        // var sipTlsChannel = new SIPTLSChannel(localhostCertificate, new IPEndPoint(IPAddress.Any, Constants.SipsListenPort));
        // sipTransport.AddSIPChannel(sipTlsChannel);
        
        sipTransport.EnableTraceLogs();
        sipTransport.SIPTransportRequestReceived += OnRequest;
        StartRegistrations(_sipAccounts);

        Console.WriteLine("Started ApartiumPhoneServer!");
        
        var localhostType = address.ToString().Equals(Constants.LocalHostIpV4Address) ? "(IPV4 localhost)" : 
            address.ToString().Equals(Constants.LocalHostIpV6Address) ? "(IPV6 localhost)" : "";
        
        Console.WriteLine("Server is running on IP: {0} {1}", address.ToString(), localhostType);
        
        Console.WriteLine("Press any key to shutdown...");
        Console.ReadKey();
        Logger.LogInformation("Exiting...");
        Logger.LogInformation("Shutting down SIP transport...");
        sipTransport.Shutdown();
    }
    
    private void SetAddress(string? ip)
    {
        while (string.IsNullOrEmpty(ip))
        {
            Console.Write("Please enter IP Address: ");
            ip = Console.ReadLine();
        }

        ip = ip.ToLower() switch
        {
            Constants.LocalHostIpv4Cmd => Constants.LocalHostAddress,
            Constants.LocalHostIpv6Cmd => Constants.LocalHostIpV6Address,
            _ => ip
        };

        IPAddress address = null;
        try
        { 
            address = IPAddress.Parse(ip);
        }
        catch (Exception _)
        {
            Console.Error.WriteLine($"Invalid IP Address: '{ip}'");
            Environment.Exit(Constants.ExitFailure);
        }
        
        this.address = address;
    }

    /// <summary>
    /// Because this is a server user agent the SIP transport must start listening for client user agents.
    /// </summary>
    private async Task OnRequest(SIPEndPoint localSipEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
    {
        if (sipRequest.Header.From is { FromTag: not null } && sipRequest.Header.To is { ToTag: not null })
        {
            // This is an in-dialog request that will be handled directly by a user agent instance.
            Console.WriteLine("I dont know what is this but okay.");
            return;
        }

        try
        {
            var sipRequestHandler = new SipRequestHandler(this, sipRequest, localSipEndPoint, remoteEndPoint);
            await sipRequestHandler.Handle();
        }
        catch (Exception e)
        {
            Logger.LogWarning($"Error occured while handling {sipRequest.Method}. {e.Message}");
        }
    }

    /// <summary>
    /// Starts a registration agent for each of the supplied SIP accounts.
    /// </summary>
    /// <param name="sipAccounts">The list of SIP accounts to create a registration for.</param>
    private void StartRegistrations(List<SipRegisterAccount> sipAccounts)
    {
        foreach (var sipAccount in sipAccounts)
        {
            var regUserAgent = new SIPRegistrationUserAgent(sipTransport, sipAccount.Username, sipAccount.Password,
                sipAccount.Domain, sipAccount.Expiry);

            // Event handlers for the different stages of the registration.
            regUserAgent.RegistrationFailed += (uri, _, err) => Logger.LogError($"{uri}: {err}");
            regUserAgent.RegistrationTemporaryFailure += (uri, _, msg) => Logger.LogWarning($"{uri}: {msg}");
            regUserAgent.RegistrationRemoved += (uri, _) => Logger.LogError($"{uri} registration failed.");
            regUserAgent.RegistrationSuccessful += (uri, _) =>
                Logger.LogInformation($"{uri} registration succeeded.");

            // Start the thread to perform the initial registration and then periodically resend it.
            regUserAgent.Start();

            _registrations.TryAdd($"{sipAccount.Username}@{sipAccount.Domain}", regUserAgent);
        }
    }
    
    /// <summary>
    /// Remove call from the active calls list.
    /// </summary>
    /// <param name="dialogue">The dialogue that was hangup.</param>
    public void OnHangup(SIPDialogue? dialogue)
    {
        if (dialogue == null)
        {
            return;
        }
            
        var callId = dialogue.CallId;
        if (!_calls.TryRemove(callId, out var sipUserAgent))
        {
            return;
        }
        
        // This app only uses each SIP user agent once so here the agent is 
        // explicitly closed to prevent is responding to any new SIP requests.
        sipUserAgent.Close();
        Console.WriteLine("User hangup the call.");
    }

    /// <summary>
    /// Event handler for receiving a DTMF tone.
    /// </summary>
    /// <param name="ua">The user agent that received the DTMF tone.</param>
    /// <param name="key">The DTMF tone.</param>
    /// <param name="duration">The duration in milliseconds of the tone.</param>
    public void OnDtmfTone(SIPUserAgent ua, byte key, int duration)
    {
        var callId = ua.Dialogue.CallId;
        Logger.LogInformation($"Call {callId} received DTMF tone {key}, duration {duration}ms.");


        var dtmfParser = new DtmfParser();
        var keyPressed = dtmfParser.Parse(key);
        
        Console.WriteLine("User pressed {0}!", keyPressed);
    }

    /// <summary>
    /// Example of how to create a basic RTP session object and hook up the event handlers.
    /// </summary>
    /// <param name="userAgent">The user agent the RTP session is being created for.</param>
    /// <param name="destination">THe destination specified on an incoming call. Can be used to
    /// set the audio source.</param>
    /// <returns>A new RTP session object.</returns>
    public VoIPMediaSession CreateRtpSession(SIPUserAgent userAgent, string destination)
    {
        var codecs = new List<AudioCodecsEnum> { AudioCodecsEnum.PCMU, AudioCodecsEnum.PCMA, AudioCodecsEnum.G722 };

        if (string.IsNullOrEmpty(destination) || !Enum.TryParse(destination, out AudioSourcesEnum audioSource))
        {
            audioSource = AudioSourcesEnum.Music;
        }

        Logger.LogInformation($"RTP audio session source set to {audioSource}.");

        var audioExtrasSource =
            new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = audioSource });
        audioExtrasSource.RestrictFormats(formats => codecs.Contains(formats.Codec));

        var rtpAudioSession = new VoIPMediaSession(new MediaEndPoints { AudioSource = audioExtrasSource });
        rtpAudioSession.AcceptRtpFromAny = true;

        // Wire up the event handler for RTP packets received from the remote party.
        rtpAudioSession.OnRtpPacketReceived += (_, type, rtp) => OnRtpPacketReceived(userAgent, type, rtp);
        rtpAudioSession.OnTimeout += _ =>
        {
            Logger.LogWarning(userAgent.Dialogue != null
                ? $"RTP timeout on call with {userAgent.Dialogue.RemoteTarget}, hanging up."
                : $"RTP timeout on incomplete call, closing RTP session.");

            userAgent.Hangup();
        };

        return rtpAudioSession;
    }
    
    /// <summary>
    /// Event handler for receiving RTP packets.
    /// </summary>
    /// <param name="userAgent">The SIP user agent associated with the RTP session.</param>
    /// <param name="type">The media type of the RTP packet (audio or video).</param>
    /// <param name="rtpPacket">The RTP packet received from the remote party.</param>
    private void OnRtpPacketReceived(SIPUserAgent userAgent, SDPMediaTypesEnum type, RTPPacket rtpPacket)
    {
        // The raw audio data is available in rtpPacket.Payload.
    }

    public bool TryAddCall(string key, SIPUserAgent userAgent)
    {
        return _calls.TryAdd(key, userAgent);
    }
    
    /// <summary>
    /// Creates a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
    /// </summary>
    private static Microsoft.Extensions.Logging.ILogger InitLogger()
    {
        var serilogLogger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
            .WriteTo.Console()
            .CreateLogger();
        
        var factory = new SerilogLoggerFactory(serilogLogger);
        SIPSorcery.LogFactory.Set(factory);
        return factory.CreateLogger<ApartiumPhoneServer>();
    }
}