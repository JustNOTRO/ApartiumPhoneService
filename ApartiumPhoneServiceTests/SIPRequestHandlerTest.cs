using System.Net;
using ApartiumPhoneService;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Moq;
using NSubstitute;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ApartiumPhoneServiceTests;

[TestSubject(typeof(SIPRequestHandler))]
public class SIPRequestHandlerTest
{
    private const string ServerFilePath =
        "/home/notro/RiderProjects/ApartiumPhoneService/ApartiumPhoneService/server.yml";

    private readonly ApartiumPhoneServer _serverMock;

    private SIPRequestHandler _sipRequestHandler;

    private readonly Mock<ISIPRequestWrapper> _sipRequestMock;
    
    private readonly Mock<LoggerWrapper> _loggerMock;
    
    private Mock<SIPUserAgentWrapper> _userAgentMock;

    private Mock<SIPServerUserAgent> _serverUserAgentMock;
    
    public SIPRequestHandlerTest()
    {
        _serverMock = Substitute.For<ApartiumPhoneServer>(ServerFilePath);
        _sipRequestMock = new Mock<ISIPRequestWrapper>();
        _loggerMock = new Mock<LoggerWrapper> { CallBase = true };
        
        MockDependencies();
    }

    [Fact]
    public void TestHandle_Incoming_Call()
    {
        _serverMock.TryAddCall(Arg.Any<string>(), Arg.Any<SIPOngoingCall>()).Returns(true);
        _sipRequestHandler.Handle().ConfigureAwait(true);
        
        Assert.Equal(SIPMethodsEnum.INVITE, _sipRequestMock.Object.sipRequest.Method);
        Assert.NotNull(_serverMock.GetSipTransport());
        
        var nextLog = _loggerMock.Object.GetNextLog();
        Assert.Contains("Incoming call request:", nextLog);
    }
    
    [Fact]
    public async Task TestHandle_Incoming_Call_When_Adding_Call_Fails()
    {
        
    }

    private void MockDependencies()
    {
        _sipRequestMock.Setup(request => request.sipRequest)
            .Returns(new SIPRequest(SIPMethodsEnum.INVITE, "sip:@500localhost"));
        _sipRequestMock.Setup(request => request.Method).Returns(SIPMethodsEnum.INVITE);

        _sipRequestMock.Object.sipRequest.Header = new SIPHeader
        {
            From = new SIPFromHeader(null, new SIPURI("sip", "fromUser", "localhost"), "fromTag"),
            To = new SIPToHeader(null, new SIPURI("sip", "toUser", "localhost"), "toTag"),
            Vias = new SIPViaSet()
        };
        
        var sipViaHeader = new SIPViaHeader("127.0.0.1", 5060, "some branch");
        _sipRequestMock.Object.sipRequest.Header.Vias.AddBottomViaHeader(sipViaHeader);
        _sipRequestMock.Object.sipRequest.Body = "body";
        
        var outboundProxy = new Mock<SIPEndPoint>(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5060));
        var sipTransportMock = new Mock<SIPTransport>();
        
        var uasInviteTransactionMock = new Mock<UASInviteTransactionWrapper>(sipTransportMock.Object,
            _sipRequestMock.Object.sipRequest,
            outboundProxy.Object,
            false);
        
        _serverUserAgentMock = new Mock<SIPServerUserAgent>(sipTransportMock.Object, 
            outboundProxy.Object,
            uasInviteTransactionMock.Object,
            new Mock<ISIPAccount>().Object);
       
        _sipRequestHandler = new SIPRequestHandler(_serverMock,
            _sipRequestMock.Object.sipRequest,
            new SIPEndPoint(IPEndPoint.Parse("127.0.0.1")),
            new SIPEndPoint(IPEndPoint.Parse("127.0.0.1")),
            _loggerMock.Object
        );
    }
}

/*
 * _userAgentMock = new Mock<SIPUserAgentWrapper>();
   _userAgentMock.Setup(agent => agent.AcceptCall(It.IsAny<SIPRequest>())).Returns(_serverUserAgentMock.Object);
   
   var voIpMediaSession = new VoIPMediaSession(new MediaEndPoints { AudioSource = new AudioExtrasSource() });
   _userAgentMock.Setup(agent => agent.Answer(_serverUserAgentMock.Object, voIpMediaSession, IPAddress.Any)).Returns(Task.FromResult(true));

   var voIpAudioPlayerMock = new Mock<VoIpAudioPlayer>();
   voIpAudioPlayerMock.Setup(audioPlayer => audioPlayer.Play(It.IsAny<VoIpSound>())).Callback(() =>
   {
       // do nothing
   });
   
   var sipDialogueMock = new Mock<SIPDialogue>();
   _userAgentMock.Setup(agent => agent.IsCallActive).Returns(true).Verifiable();
   _userAgentMock.Setup(userAgent => userAgent.Dialogue).Returns(sipDialogueMock.Object).Verifiable();
 */

public class LoggerWrapper : ILogger
{
    private readonly ILogger _logger;
    
    private readonly Queue<string> _logs = new();

    public LoggerWrapper()
    {
        _logger = InitLogger();
    }
    
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
    
    public virtual void Log<TState>(
        LogLevel logLevel, 
        EventId eventId, 
        TState state, 
        Exception? exception, 
        Func<TState, Exception?, string> formatter)
    {
        _logger.Log(logLevel, eventId, state, exception, formatter);

        var message = formatter.Invoke(state, exception);
        _logs.Enqueue(message);
    }

    public String GetNextLog()
    {
        if (_logs.Count == 0)
        {
            throw new InvalidOperationException("No more logs were received.");
        }
        
        return _logs.Dequeue();
    }
    
    public bool IsEnabled(LogLevel logLevel)
    {
        return _logger.IsEnabled(logLevel);
    }
    
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _logger.BeginScope(state);
    }
    
    public virtual void LogInformation(string message, params object[] args)
    {
        _logger.LogInformation(message, args);
        _logs.Enqueue(message);
    }

    public virtual void LogWarning(string message, params object[] args)
    {
        _logger.LogWarning(message, args);
        _logs.Enqueue(message);
    }
}

public class UASInviteTransactionWrapper(
    SIPTransport sipTransport,
    SIPRequest sipRequest,
    SIPEndPoint outboundProxy,
    bool noCdr)
    : UASInviteTransaction(sipTransport, sipRequest, outboundProxy, noCdr);