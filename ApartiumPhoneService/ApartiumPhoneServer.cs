using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace ApartiumPhoneService;

public class ApartiumPhoneServer
{
    private const int SipListenPort = 5060;
    private const string LocalHostAddress = "127.0.0.1";
    private const string LocalHostIpV4Address = "127.0.0.1";
    private const string LocalHostIpV6Address = "::1";
    private const string LocalHostIpv4Cmd = "lv4";
    private const string LocalHostIpv6Cmd = "lv6";
    public const string SipsCertificatePath = "localhost.pfx";

    private readonly ConcurrentDictionary<string, SIPUserAgent> _calls = new();

    private readonly ConfigDataProvider _configDataProvider;

    private readonly SIPTransport _sipTransport;

    private static readonly Microsoft.Extensions.Logging.ILogger Logger = InitLogger();

    private IPAddress _address;

    public ApartiumPhoneServer(string serverFilePath)
    {
        _sipTransport = new SIPTransport();
        _configDataProvider = new ConfigDataProvider(serverFilePath);
    }

    public static Microsoft.Extensions.Logging.ILogger GetLogger()
    {
        return Logger;
    }

    public SIPTransport GetSipTransport()
    {
        return _sipTransport;
    }

    private bool ValidateIPAddress(string? ip)
    {
        if (string.IsNullOrEmpty(ip))
        {
            Console.WriteLine();
            Console.WriteLine("IP Address cannot be empty.");
            return false;
        }
        
        ip = ip.ToLower() switch
        {
            LocalHostIpv4Cmd => LocalHostAddress,
            LocalHostIpv6Cmd => LocalHostIpV6Address,
            _ => ip
        };

        IPAddress address = null;
        try
        {
            address = IPAddress.Parse(ip);
        }
        catch (Exception _)
        {
            Console.WriteLine();
            Console.WriteLine($"Invalid IP Address: '{ip}'");
            return false;
        }

        _address = address;
        return true;
    }
    
    public void Start()
    {
        Console.WriteLine("-----Lazy commands-----");
        Console.WriteLine("{0} - equivalent to {1} (IPV4 localhost)", LocalHostIpv4Cmd, LocalHostIpV4Address);
        Console.WriteLine("{0} - equivalent to {1} (IPV6 localhost)", LocalHostIpv6Cmd, LocalHostIpV6Address);
        Console.WriteLine();
        
        Console.Write("Please enter IP Address: ");
        var ipAddress = Console.ReadLine();
        if (!ValidateIPAddress(ipAddress))
        {
            return;
        }

        _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(_address, SipListenPort)));

        // var localhostCertificate = new X509Certificate2(Constants.SipsCertificatePath);
        // var sipTlsChannel = new SIPTLSChannel(localhostCertificate, new IPEnPoint(IPAddress.Any, Constants.SipsListenPort));
        // sipTransport.AddSIPChannel(sipTlsChannel);

        _sipTransport.EnableTraceLogs();
        StartRegistrations();
        _sipTransport.SIPTransportRequestReceived += OnRequest;

        var localhostType = _address.ToString().Equals(LocalHostIpV4Address) ? "(IPV4 localhost)" :
            _address.ToString().Equals(LocalHostIpV6Address) ? "(IPV6 localhost)" : "";

        Console.WriteLine();
        Console.WriteLine("Started ApartiumPhoneServer!");
        Console.WriteLine("Server is running on IP: {0} {1}", _address.ToString(), localhostType);
        
        Console.WriteLine("Press any key to shutdown...");

        if (Console.LargestWindowWidth != 0)
        {
            Console.ReadKey(true);
        }
        
        Logger.LogInformation("Exiting...");
        Logger.LogInformation("Shutting down SIP transport...");
        _sipTransport.Shutdown();
        _calls.Clear();
    }

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
    private void StartRegistrations()
    {
        var sipAccounts = _configDataProvider.GetRegisteredAccounts();

        foreach (var sipAccount in sipAccounts)
        {
            var userName = sipAccount.Username;
            var domain = sipAccount.Domain;

            var regUserAgent = new SIPRegistrationUserAgent(_sipTransport, userName, sipAccount.Password,
                domain, sipAccount.Expiry);

            // Event handlers for the different stages of the registration.
            regUserAgent.RegistrationFailed += (uri, _, err) => Logger.LogError($"{uri}: {err}");
            regUserAgent.RegistrationTemporaryFailure += (uri, _, msg) => Logger.LogWarning($"{uri}: {msg}");
            regUserAgent.RegistrationRemoved += (uri, _) => Logger.LogError($"{uri} registration failed.");
            regUserAgent.RegistrationSuccessful += (uri, _) =>
                Logger.LogInformation($"{uri} registration succeeded.");

            regUserAgent.UserDisplayName = userName;

            // Start the thread to perform the initial registration and then periodically resend it.
            regUserAgent.Start();
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

        rtpAudioSession.OnTimeout += _ =>
        {
            Logger.LogWarning(userAgent.Dialogue != null
                ? $"RTP timeout on call with {userAgent.Dialogue.RemoteTarget}, hanging up."
                : $"RTP timeout on incomplete call, closing RTP session.");

            userAgent.Hangup();
        };

        return rtpAudioSession;
    }

    public void TryAddCall(string key, SIPUserAgent userAgent)
    {
        _calls.TryAdd(key, userAgent);
    }

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