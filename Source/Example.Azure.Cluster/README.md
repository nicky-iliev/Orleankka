### Prerequisites

* Azure SDK 2.8
* Azure Compute Emulator v2.8

### How to run

* Build solution and make sure there is no build errors due to missing packages
* Choose Deployment project to be a "Startup project"
* Hit F5 to deploy everything into emulator
* In compute emulator you should see 2 roles: Client (web role) running 1 instance, and Cluster (worker role) running 1 instance
* Client is an ASP.NET application and after the role is started up, the Visual Studio should open the entry page in your browser
* Enter the count of publisher grains you want to spawn and click "Spawn"
* After some delay you will be redirected to notifications page where you can observe all events generated by all grains in a cluster
* You can click "Back" in a browser and spawn more grains any time you wish

There could be some glitches:

* You may receive `System.TimeoutException: Task.WaitWithThrow has timed out after 00:00:03` - just Continue
* Trying to open Compute Emulator UI may give you "Unable to determine the emulator running mode. Please ensure you have sufficient permissions". See the SO answer [here]()http://stackoverflow.com/a/18911982.

### Have fun!

P.S. If you want to run Orleans cluster in multi-node setup you will need to:

* Open Deployment project properties
* On Web tab select Use Full Emulator
* Restart VS in elevated mode
* Also make sure to restart Compute Emulator in elevated mode as well
* Hit F5 to deploy roles into emulator
* This time there will be 3 instances of Cluster worker role
* Now you can play again.
* Observe that grain notifications now came from different nodes

### Have even more fun!