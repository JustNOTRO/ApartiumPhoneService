using ApartiumPhoneService;
using JetBrains.Annotations;
using NSubstitute;
using Xunit.Abstractions;

namespace ApartiumPhoneServiceTests;

[TestSubject(typeof(ApartiumPhoneServer))]
public class ApartiumPhoneServerTest
{
    private const string ServerFilePath = "/home/notro/RiderProjects/ApartiumPhoneService/ApartiumPhoneService/server.yml";
    
    private readonly ITestOutputHelper _testOutputHelper;
    
    private readonly ApartiumPhoneServer _apartiumPhoneServer;

    private readonly ConfigDataProvider _configDataProviderMock;
    
    private readonly AccountsProvider _accountsProviderMock;
    
    public ApartiumPhoneServerTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _apartiumPhoneServer = new ApartiumPhoneServer(ServerFilePath);
        _configDataProviderMock = Substitute.For<ConfigDataProvider>(ServerFilePath);
        _accountsProviderMock = Substitute.For<AccountsProvider>();
    }

    [Fact]
    public void TestStart()
    {
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
        Assert.NotNull(ApartiumPhoneServer.GetLogger());
        Assert.Equal(loggerFormat + "Exiting...", lines[7]);
        Assert.Equal(loggerFormat + "Shutting down SIP transport...", lines[8]);
    }

    [Fact]
    public void TestStart_When_IPAddress_Is_Not_Provided()
    {
        var sipRegisteredAccount = new SIPRegisteredAccount
        {
            Username = "555",
            Password = "123",
            Domain = "localhost",
            Expiry = 120
        };

        _accountsProviderMock.Accounts.Returns([sipRegisteredAccount]);
        _configDataProviderMock.GetRegisteredAccounts().Returns([sipRegisteredAccount]);
        
        Console.SetIn(new StringReader(""));
        var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);
        _apartiumPhoneServer.Start();
        
        var lines = stringWriter.ToString().Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        
        Assert.Equal("-----Lazy commands-----", lines[0]);
        Assert.Equal("lv4 - equivalent to 127.0.0.1 (IPV4 localhost)", lines[1]);
        Assert.Equal("lv6 - equivalent to ::1 (IPV6 localhost)", lines[2]);

        Assert.Equal("Please enter IP Address: ", lines[3]);
        Assert.Equal("IP Address cannot be empty.", lines[4]);
        
        Assert.NotNull(_apartiumPhoneServer.GetSipTransport());
    }
    
    [Fact]
    public void TestStart_When_IPAddress_Is_Invalid()
    {
        var sipRegisteredAccount = new SIPRegisteredAccount
        {
            Username = "555",
            Password = "123",
            Domain = "localhost",
            Expiry = 120
        };

        _accountsProviderMock.Accounts.Returns([sipRegisteredAccount]);
        _configDataProviderMock.GetRegisteredAccounts().Returns([sipRegisteredAccount]);
        
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
}