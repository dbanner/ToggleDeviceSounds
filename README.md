This turns off the annoying USB connect/disconnect notifications when these portable external monitors are connected. When the monitor is disconnected, the notifications come back on.

I have this program starting up when I log in. This is done via Task Scheduler:
1. Create a Basic Task.
2. Trigger: "When I log on"
3. Action: "Start a program"
4.   - Program: <Path to your compiled version of this app"
     - Add arguments: -logFile="C:\temp"
5. Finish

It will run headlessly and log to C:\temp, creating a new log file each time it starts up. (Only the latest 10 log files will be kept.)

If you need to kill it, do so via Task Manager.
