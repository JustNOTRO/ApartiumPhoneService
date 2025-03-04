using System.Runtime.InteropServices;
using ApartiumPhoneService;

Console.WriteLine("Starting ApartiumPhoneService.. Hold tight!");
var projectDirectory = Directory.GetParent(Environment.CurrentDirectory)?.Parent?.Parent?.FullName;
var serverFilePath = projectDirectory + "/server.yml";
var server = new ApartiumPhoneServer(serverFilePath);
server.Start();



