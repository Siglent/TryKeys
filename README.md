TryKeys - a .NET Core 2.1 utility - Companion application to FindKeys  

The purpose of this utility is to recover the valid keys you licensed with your scope but you lost the paperwork for and 
you do not remember the codes for. 

First you generate a list of possible keys using the FindKeys utility. Edit the output of FindKeys to remove any keys which 
are not likely candidates (e.g. real life words). Then, execute this utility using that file as an input source.

Upon startup, the program reads the contents of "TryKeys.json" to configure various parameters needed for it to work. The 
options in this file and their purpose are as follows:

keyfile: The fully-qualified path to the list of keys you wish to try, e.g. "g:trykeys.txt"

scopeip: the IP address of the scope, needed for web and telnet access

port: the telnet port the program should connect to. Default '23'.

username: The telnet username. Default is "root".

password: The telnet password. Default is "eevblog"

bandwidth: Tells the program to not only attempt to find any missing option licenses, but also the maximum bandwidth license.


Theory of operation:

The program cycles through a list of keys contained in the key file. For option licenses, it issues the "license install" 
SCPI command through the web interface. It uses the telnet connection to determine if the option license file was created 
in /usr/bin/siglent/firmdata0 after issuing the command. If the file exists, then the key used for the license install 
command was the 'correct' one.

For bandwidth licenses, the program determines the current bandwidth license key from the firmdata0 directory, and what 
that key is good for, by using the PRBD SCPI command. Then, it cycles through the keys, issuing the MCBD with they test 
key, and then re-examines the output of PRBD to determine if the bandwidth has changed. If so, it determines if the 
bandwidth increased -- in which case, it will check to see if the maximum bandwidth has been reached with the key. If the 
bandwith decreased, then it re-issues the MCBD commmand to 're-install' the 'current' bandwidth license key so scope 
bandwidth will not decrease.

A log is dumped to the console. Upon program completion, the scope will be restarted if the bandwidth was changed. This 
is necessary for the new bandwidth to take effect. Finally, a summary of license keys located will be printed. 

To execute from the command line:   dotnet TryKeys.dll  

Sample log file:

Execution starts @ 10/4/2018 8:58 PM  
Scope Option 'AWG' not licensed, will seek key  
Scope Option 'MSO' not licensed, will seek key  
Scope Option 'WIFI' not licensed, will seek key  
Scope bandwidth license key: VVVVVVVVVVVVVVV  
We have 584 keys to try for 4 options  
Scope bandwidth currently licensed: 50M of 200M  
100M Bandwidth license key found: 1111111111111111  
Maximum bandwidth (200M) license key found: 2222222222222222  
Scope Option 'AWG' license key found: AAAAAAAAAAAAAAAA  
Scope Option 'MSO' license key found: MMMMMMMMMMMMMMMM  
Scope Option 'WIFI' license key found: WWWWWWWWWWWWWWWW  
  
Summary of License keys located:  
200M bandwidth license key: 2222222222222222  
AWG license key: AAAAAAAAAAAAAAAA  
MSO license key: MMMMMMMMMMMMMMMM  
WIFI license key: WWWWWWWWWWWWWWWW  
  
Rebooting scope to activate higher bandwidth license.  
  
Execution ends @ 10/4/2018 9:03 PM  

You can verify the presence of your recovered license keys on the scope's 'options' screen. You should 
print a copy of your recovered license keys and keep them in a safe place for future reference.

To revert the scope back to the previous bandwidth license, and to remove the optional licenses, you 
execute the following script after logging in via a telnet session as root:

mount -o remount,rw /usr/bin/siglent/firmdata0  
rm /usr/bin/siglent/firmdata0/options*  
cat VVVVVVVVVVVVVVVV > /usr/bin/siglent/firmdata0  
(control-d)(control-d)  
sync  
reboot  


This program has several dependencies you must install through the NuGet Package Manager.  
They are: Microsoft.Extensions.Configuration, Newtonsoft.Json, and Telnet (from 9swampy).

Note: at the moment this utility only supports the SDS####X-E series of scopes, however, additional  
functionality will be added as details become available.

Special thanks to eevblog user tv84 who gave me tons of assistance during the development of this utility.