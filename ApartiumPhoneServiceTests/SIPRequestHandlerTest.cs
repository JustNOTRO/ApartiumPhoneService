using System.Collections.Concurrent;
using System.Net;
using ApartiumPhoneService;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Moq;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using Xunit.Sdk;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ApartiumPhoneServiceTests;

[TestSubject(typeof(SIPRequestHandler))]
public class SIPRequestHandlerTest
{
    private SIPRequestHandler _sipRequestHandler;

    private readonly Mock<ApartiumPhoneServer> _serverMock = new("dummy path");

    private SIPRequest _sipRequest;
    private readonly Mock<SIPEndPoint> _sipEndPointMock = new(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5060));
    private readonly Mock<SIPEndPoint> _remoteEndPointMock = new(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5060));
    private readonly Mock<SIPUserAgentFactory> _sipUaFactoryMock = new();
    private readonly Mock<LoggerWrapper> _loggerMock = new() { CallBase = true };
    
    private Mock<SIPUserAgentWrapper> _userAgentMock;
    private Mock<UASInviteTransactionWrapper> _uasInvTransactionMock;
    private Mock<SIPServerUserAgentWrapper> _serverUaMock = new();
    
    private readonly Mock<ConcurrentDictionary<char, VoIpSound>> _keySoundsMock = new();
    private readonly Mock<ConcurrentBag<char>> _keysPressed = new();
    
    private readonly Mock<VoIpAudioPlayer> _voIpAudioPlayerMock = new();
    
    public SIPRequestHandlerTest()
    {
        // Arrange
        var sipTransportMock = new Mock<SIPTransport>();
        SetupSIPUserAgent(sipTransportMock.Object);
        
        _serverMock.Setup(x => x.GetSipTransport())
            .Returns(sipTransportMock.Object)
            .Verifiable();

        _voIpAudioPlayerMock.Setup(x => x.Play(It.IsAny<VoIpSound>())).Verifiable();
    }
    
    [Fact]
    public async Task TestHandle_Incoming_Call()
    {
        // Arrange
        _serverMock.Setup(x => x.TryAddCall(It.IsAny<string>(), It.IsAny<SIPOngoingCall>()))
            .Returns(true)
            .Verifiable();

        // Act
        _sipRequestHandler = new SIPRequestHandler(_serverMock.Object,
            _sipUaFactoryMock.Object,
            _voIpAudioPlayerMock.Object,
            _loggerMock.Object);
        
        await _sipRequestHandler.Handle(_sipRequest, _sipEndPointMock.Object, _remoteEndPointMock.Object);
        
        // Assert
        var nextLog = _loggerMock.Object.GetNextLog();
        Assert.Contains("Incoming call request: ", nextLog);
        AssertNoMoreLogs();
        Assert.NotNull(_userAgentMock.Object);
    }

    [Fact]
    public async Task TestHandle_Incoming_Call_When_Adding_Call_Fails()
    {
        // Act
        _sipRequestHandler = new SIPRequestHandler(_serverMock.Object,
            _sipUaFactoryMock.Object,
            _voIpAudioPlayerMock.Object,
            _loggerMock.Object);
        
        await _sipRequestHandler.Handle(_sipRequest, _sipEndPointMock.Object, _remoteEndPointMock.Object);
        
        // Assert
        var nextLog = _loggerMock.Object.GetNextLog();
        Assert.Contains("Incoming call request: ", nextLog);
        
        nextLog = _loggerMock.Object.GetNextLog();
        Assert.Contains("Could not add call to active calls", nextLog);
        AssertNoMoreLogs();
        Assert.NotNull(_userAgentMock.Object);
    }

    [Fact]
    public async Task TestHandle_Incoming_Call_When_Call_Cancelled()
    {
        // Arrange
        _serverMock.Setup(x => x.TryAddCall(It.IsAny<string>(), It.IsAny<SIPOngoingCall>()))
            .Returns(true)
            .Verifiable();
        
        _serverUaMock.Setup(x => x.IsCancelled)
            .Returns(true)
            .Verifiable();
        
        _serverUaMock.Object.SetCancelled(true);
        
        // Act
        _sipRequestHandler = new SIPRequestHandler(_serverMock.Object,
            _sipUaFactoryMock.Object,
            _voIpAudioPlayerMock.Object,
            _loggerMock.Object);
        
        await _sipRequestHandler.Handle(_sipRequest, _sipEndPointMock.Object, _remoteEndPointMock.Object);
        
        // Assert
        Assert.Equal(2, _loggerMock.Object.GetLogsCount());
        
        var nextLog = _loggerMock.Object.GetNextLog();
        Assert.Contains("Incoming call request: ", nextLog);
        
        nextLog = _loggerMock.Object.GetNextLog();
        Assert.Contains("Incoming call cancelled by remote party.", nextLog);
        AssertNoMoreLogs();
    }

    [Fact]
    public async Task TestHandle_Incoming_Call_On_Hangup_When_Call_Null()
    {
        // Arrange
        _userAgentMock.Object.Dialogue().CallId = "123";
        
        _serverMock.Setup(x => x.TryRemoveCall(It.Is<string>(callId => callId != "123")))
            .Returns((SIPOngoingCall)null)
            .Verifiable();
        
        _serverMock.Setup(x => x.TryAddCall(It.IsAny<string>(), It.IsAny<SIPOngoingCall>()))
            .Returns(true)
            .Verifiable();
        
        _userAgentMock.Setup(x => x.IsCallActive)
            .Returns(true)
            .Verifiable();
        
        // Act
        _sipRequestHandler = new SIPRequestHandler(
            _serverMock.Object,
            _sipUaFactoryMock.Object,
            _voIpAudioPlayerMock.Object,
            _loggerMock.Object
        );
        
        await _sipRequestHandler.Handle(_sipRequest,
            _sipEndPointMock.Object,
            _remoteEndPointMock.Object
        );
        
        // Assert
        var nextLog = _loggerMock.Object.GetNextLog();
        Assert.Contains("Incoming call request: ", nextLog);
        AssertNoMoreLogs();

        Assert.NotNull(_userAgentMock.Object.Dialogue().CallId);
        Assert.Null(_serverMock.Object.TryRemoveCall("134"));
    }

    private void AssertNoMoreLogs()
    {
        var logsCount = _loggerMock.Object.GetLogsCount();

        if (logsCount != 0)
        {
            throw new InvalidOperationException("More logs were received");
        }
    }

    private void SetupSIPUserAgent(SIPTransport sipTransport)
    {
        _sipRequest = new SIPRequest(SIPMethodsEnum.INVITE, "sip:500@localhost")
        {
            Method = SIPMethodsEnum.INVITE,
            Header = new SIPHeader
            {
                Vias = new SIPViaSet()
            }
        };
        
        _sipRequest.Header.Vias.PushViaHeader(new SIPViaHeader());
        _sipRequest.Header.Vias.TopViaHeader.Host = "127.0.0.1";
        _sipRequest.Header.From = new SIPFromHeader("sip:bob@example.com", SIPURI.ParseSIPURI("sips:notro@localhost"), "notro");
        _sipRequest.Header.To = new SIPToHeader("sip:notro@example.com", SIPURI.ParseSIPURI("sips:notro@localhost"), "server");
        _sipRequest.Body = "body";

        var localhost = IPAddress.Parse("127.0.0.1");
        var dummyEndPoint = new SIPEndPoint(new IPEndPoint(localhost, 5060));
        
        _userAgentMock = new Mock<SIPUserAgentWrapper>(sipTransport, null);
        _uasInvTransactionMock = new Mock<UASInviteTransactionWrapper>(sipTransport, _sipRequest, dummyEndPoint, false);
        _serverUaMock = new Mock<SIPServerUserAgentWrapper>(sipTransport, null, _uasInvTransactionMock.Object, new Mock<ISIPAccount>().Object);
        
        _sipUaFactoryMock.Setup(x => x.Create(It.IsAny<SIPTransport>(), It.IsAny<SIPEndPoint>()))
            .Returns(_userAgentMock.Object)
            .Verifiable();
        
        _userAgentMock.Setup(x => x.AcceptCall(It.IsAny<SIPRequest>()))
            .Returns(_serverUaMock.Object)
            .Verifiable();

        _userAgentMock.Setup(x => x.Answer(_serverUaMock.Object, It.IsAny<VoIPMediaSession>(), localhost))
            .Returns(Task.FromResult(true))
            .Verifiable();
        
        _userAgentMock.Setup(x => x.Dialogue())
            .Returns(new SIPDialogue())
            .Verifiable();
    }
}

public class SIPServerUserAgentWrapper(SIPTransport sipTransport, SIPEndPoint outboundProxy, UASInviteTransactionWrapper inviteTransaction, ISIPAccount account) 
    : SIPServerUserAgent(sipTransport, outboundProxy, inviteTransaction, account)
{

    public void SetCancelled(bool state)
    {
        m_isCancelled = state;
    }
    
    public new virtual bool IsCancelled => base.IsCancelled;
}

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

    public int GetLogsCount()
    {
        return _logs.Count;
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
    : UASInviteTransaction(sipTransport, sipRequest, outboundProxy, noCdr)
{
    public virtual void CancelCall(SIPRequest? sipRequest = null)
    {
        base.CancelCall(sipRequest);
    }
}