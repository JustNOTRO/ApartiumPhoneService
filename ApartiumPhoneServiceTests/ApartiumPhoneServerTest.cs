using System.Net;
using ApartiumPhoneService;
using JetBrains.Annotations;
using Moq;
using NSubstitute;
using SIPSorcery.SIP;

namespace ApartiumPhoneServiceTests;

[TestSubject(typeof(ApartiumPhoneServer))]
public class ApartiumPhoneServerTest
{
    private const string ServerFilePath =
        "/home/notro/RiderProjects/ApartiumPhoneService/ApartiumPhoneService/Data/server.yml";
    
    private readonly ApartiumPhoneServer _apartiumPhoneServer;

    private readonly ConfigDataProvider _configDataProviderMock;

    private readonly AccountsProvider _accountsProviderMock;

    public ApartiumPhoneServerTest()
    {
        _apartiumPhoneServer = new ApartiumPhoneServer(ServerFilePath);
        _configDataProviderMock = Substitute.For<ConfigDataProvider>(ServerFilePath);
        _accountsProviderMock = Substitute.For<AccountsProvider>();
        
        var sipRegisteredAccount = new SIPRegisteredAccount
        {
            Username = "555",
            Password = "123",
            Domain = "localhost",
            Expiry = 120
        };

        _accountsProviderMock.Accounts.Returns([sipRegisteredAccount]);
        _configDataProviderMock.GetRegisteredAccounts().Returns([sipRegisteredAccount]);
        
        Console.SetIn(new StringReader("lv4"));
    }

    [Fact]
    public void TestStart()
    {
        var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);
        _apartiumPhoneServer.Start();

        var lines = stringWriter.ToString().Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("-----Lazy commands-----", lines[0]);
        Assert.Equal("lv4 - equivalent to 127.0.0.1 (IPV4 localhost)", lines[1]);
        Assert.Equal("lv6 - equivalent to ::1 (IPV6 localhost)", lines[2]);
        Assert.Equal("Please enter IP Address: ", lines[3]);
        Assert.NotNull(_apartiumPhoneServer.GetSipTransport());

        Assert.Equal("Started ApartiumPhoneServer!", lines[4]);
        Assert.Equal("Server is running on IP: 127.0.0.1 (IPV4 localhost)", lines[5]);
        Assert.Equal("Press any key to shutdown...", lines[6]);
        Assert.True(Console.LargestWindowWidth == 0);

        var currentTime = DateTime.Now.ToString("HH:mm:ss");
        var loggerFormat = $"[{currentTime} INF] ";
        Assert.Equal(loggerFormat + "Exiting...", lines[7]);
        Assert.Equal(loggerFormat + "Shutting down SIP transport...", lines[8]);
    }

    [Fact]
    public void TestStart_When_IPAddress_Is_Not_Provided()
    {
        var input = new StringReader("");
        Console.SetIn(input);
        var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);
        _apartiumPhoneServer.Start();

        var lines = stringWriter.ToString().Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("-----Lazy commands-----", lines[0]);
        Assert.Equal("lv4 - equivalent to 127.0.0.1 (IPV4 localhost)", lines[1]);
        Assert.Equal("lv6 - equivalent to ::1 (IPV6 localhost)", lines[2]);
        Assert.Equal("Please enter IP Address: ", lines[3]);
        Assert.Equal("IP Address cannot be empty.", lines[4]);
        
        Assert.True(string.IsNullOrEmpty(input.ReadToEnd()));
        Assert.NotNull(_apartiumPhoneServer.GetSipTransport());
    }
    
    [Fact]
    public void TestStart_When_IPAddress_Is_Invalid()
    {
        Console.SetIn(new StringReader("abc"));
        var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);
        _apartiumPhoneServer.Start();
        
        var lines = stringWriter.ToString().Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        
        Assert.Equal("-----Lazy commands-----", lines[0]);
        Assert.Equal("lv4 - equivalent to 127.0.0.1 (IPV4 localhost)", lines[1]);
        Assert.Equal("lv6 - equivalent to ::1 (IPV6 localhost)", lines[2]);
        Assert.Equal("Please enter IP Address: ", lines[3]);
        Assert.Equal("Invalid IP Address: 'abc'", lines[4]);
        Assert.NotNull(_apartiumPhoneServer.GetSipTransport());
    }

    [Fact]
    public void TestOnRequest_When_In_Dialog()
    {
        var mockSipRequest = new Mock<ISIPRequestWrapper>();
        var sipHeader = new SIPHeader
        {
            From = new SIPFromHeader(null, new SIPURI("sip", "fromUser", "localhost"), "fromTag"),
            To = new SIPToHeader(null, new SIPURI("sip", "toUser", "localhost"), "toTag")
        };
        
        mockSipRequest.Setup(request => request.Header).Returns(sipHeader);
        
        var input = new StringReader("lv6");
        Console.SetIn(input);
        _apartiumPhoneServer.Start();
        
        // Act
        var fromTag = mockSipRequest.Object.Header.From.FromTag;
        var toTag = mockSipRequest.Object.Header.To.ToTag;

        // Assert
        Assert.NotNull(fromTag);
        Assert.NotNull(toTag);
        Assert.Equal("fromTag", fromTag);
        Assert.Equal("toTag", toTag);
    }

    [Fact]
    public void TestStart_When_EndPoint_Is_Taken()
    {
        var sipTransport = new SIPTransport();
        try
        {
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5060)));
        }
        catch (Exception _)
        {
            // ignored
        }

        var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);
        _apartiumPhoneServer.Start();
        
        var lines = stringWriter.ToString().Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("-----Lazy commands-----", lines[0]);
        Assert.Equal("lv4 - equivalent to 127.0.0.1 (IPV4 localhost)", lines[1]);
        Assert.Equal("lv6 - equivalent to ::1 (IPV6 localhost)", lines[2]);
        Assert.Equal("Please enter IP Address: ", lines[3]);
        Assert.Equal("Unable to bind socket using end point 127.0.0.1:5060.", lines[4]);
    }
}

public interface ISIPRequestWrapper
{
    SIPMethodsEnum Method { get; set; }
    SIPURI URI { get; set; }
    SIPRequest sipRequest { get; set; }
    SIPHeader Header { get; set; }
    // Add other necessary properties and methods
}

public class SIPRequestWrapper(SIPRequest sipRequest) : ISIPRequestWrapper
{
    public virtual SIPMethodsEnum Method
    {
        get => sipRequest.Method;
        set => sipRequest.Method = value;
    }

    public SIPURI URI
    {
        get => sipRequest.URI;
        set => sipRequest.URI = value;
    }

    public SIPRequest sipRequest { get; set; }

    public SIPHeader Header
    {
        get => sipRequest.Header;
        set => sipRequest.Header = value;
    }
}

