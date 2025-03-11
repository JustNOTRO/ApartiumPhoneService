using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace ApartiumPhoneService;

/// <summary>
///  <c>ApartiumPhoneServer</c> is the class that manages the SIP Server
/// </summary>
public class ApartiumPhoneServer
{
    private const int SipListenPort = 5060;
    private const string LocalHostIpV4Address = "127.0.0.1";
    private const string LocalHostIpV6Address = "::1";
    private const string LocalHostIpv4Cmd = "lv4";
    private const string LocalHostIpv6Cmd = "lv6";

    /// <summary>
    /// a dictionary that keeps track of an active calls on the server
    /// </summary>
    private readonly ConcurrentDictionary<string, SIPOngoingCall> _calls = new();

    /// <summary>
    /// Provides our registered account and other related data
    /// </summary>
    private readonly ConfigDataProvider _configDataProvider;

    /// <summary>
    /// The server sip transport that manages channels
    /// </summary>
    private readonly SIPTransport _sipTransport;

    /// <summary>
    /// Our logger for logging errors and warnings
    /// </summary>
    private static readonly Microsoft.Extensions.Logging.ILogger Logger = InitLogger<ApartiumPhoneServer>();

    /// <summary>
    /// The server ip address
    /// </summary>
    private IPAddress _address;

    /// <summary>
    /// Constructs a new SIP server
    /// </summary>
    /// <param name="serverFilePath">The file path of the server.yml</param>
    public ApartiumPhoneServer(string serverFilePath)
    {
        _sipTransport = new SIPTransport();
        _configDataProvider = new ConfigDataProvider(serverFilePath);
    }

    /// <summary>
    /// Gets the sip transport of our server
    /// </summary>
    /// <returns></returns>
    public SIPTransport GetSipTransport()
    {
        return _sipTransport;
    }

    /// <summary>
    /// Checks for ip address validation
    /// </summary>
    /// <param name="ip">The ip address that needs to be checked</param>
    /// <returns>true if valid, false otherwise</returns>
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
            LocalHostIpv4Cmd => LocalHostIpV4Address,
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

    /// <summary>
    /// Starts the server
    /// </summary>
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

        try
        {
            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(_address, SipListenPort)));
        }
        catch (ApplicationException e)
        {
            Console.WriteLine();
            Console.WriteLine(e.Message);
            return;
        }

        //_sipTransport.EnableTraceLogs();
        StartRegistrations();
        _sipTransport.SIPTransportRequestReceived += OnRequest;

        var localhostType = _address.ToString().Equals(LocalHostIpV4Address) ? "(IPV4 localhost)" :
            _address.ToString().Equals(LocalHostIpV6Address) ? "(IPV6 localhost)" : "";

        Console.WriteLine();
        Console.WriteLine("Started ApartiumPhoneServer!");
        Console.WriteLine("Server is running on IP: {0} {1}", _address.ToString(), localhostType);

        Console.WriteLine("Press any key to shutdown...");

        // Ensuring it is not a test case
        if (Console.LargestWindowWidth != 0)
        {
            Console.ReadKey(true);
            
            foreach (var call in _calls.Values)
            {
                call.Hangup();
            }
        }

        Logger.LogInformation("Exiting...");
        Logger.LogInformation("Shutting down SIP transport...");
        _sipTransport.Shutdown();
    }

    /// <summary>
    /// Handles a request from clients
    /// </summary>
    /// <param name="localSipEndPoint">the local end point</param>
    /// <param name="remoteEndPoint">the remote end point</param>
    /// <param name="sipRequest">the sip request</param>
    private async Task OnRequest(SIPEndPoint localSipEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
    {
        if (sipRequest.Header.From.FromTag != null && sipRequest.Header.To.ToTag != null)
        {
            return;
        }

        var sipRequestHandler = new SIPRequestHandler(this, sipRequest, localSipEndPoint, remoteEndPoint, InitLogger<SIPRequestHandler>());
        await sipRequestHandler.Handle();
    }
    
    /// <summary>
    /// Starts accounts registrations
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
    /// Tries to add the call to the active calls
    /// </summary>
    /// <param name="callId">the call id</param>
    /// <param name="call">the ongoing call</param>
    /// <returns>True if succeeded, false otherwise</returns>
    public virtual bool TryAddCall(string callId, SIPOngoingCall call)
    {
        return _calls.TryAdd(callId, call);
    }
    
    /// <summary>
    /// Tries to remove the active call
    /// </summary>
    /// <param name="callId"></param>
    /// <returns>the removed call, otherwise null</returns>
    public virtual SIPOngoingCall? TryRemoveCall(string callId)
    {
        _calls.TryRemove(callId, out var call);
        return call;
    }

    /// <summary>
    /// Initializes the server logger
    /// </summary>
    /// <returns>the server logger</returns>
    private static Microsoft.Extensions.Logging.ILogger<T> InitLogger<T>()
    {
        var serilogLogger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
            .WriteTo.Console()
            .CreateLogger();

        var factory = new SerilogLoggerFactory(serilogLogger);
        SIPSorcery.LogFactory.Set(factory);
        return factory.CreateLogger<T>();
    }
}