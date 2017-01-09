﻿open System
open System.Reflection
open Orleans.Runtime.Configuration

open Orleankka
open Orleankka.FSharp
open Orleankka.FSharp.Configuration

open Messages
open Client.ChatClient

let rec handleUserInput client = task {
   
   let message = Console.ReadLine();

   match message with
   | "::leave" -> do! leave client
   
   | _         -> do! say client message
                  return! handleUserInput client
}

let startChatClient (system:IActorSystem) userName roomName = task {

   let userPath = ActorPath.From("ChatUser", userName)
   let userActor = ActorSystem.actorOfPath system userPath
   let roomStream = ActorSystem.streamOf system "sms2" roomName
   
   let chatClient = { UserName = userName; User = userActor;
                      RoomName = roomName; Room = roomStream;
                      Subscription = None }

   printfn "Joining the room '%s'..." roomName

   let! joinedClient = join chatClient

   printfn "Joined the room '%s'..." roomName
   
   return! handleUserInput joinedClient
}


[<EntryPoint>]
let main argv = 

   printfn "Please wait until Chat Server has completed boot and then press enter. \n"
   Console.ReadLine() |> ignore

   let assembly = Assembly.GetExecutingAssembly()   

   let config = ClientConfig.loadFromResource assembly "Client.xml"   
                |> ClientConfig.registerStreamProvider<Orleans.Providers.Streams.SimpleMessageStream.SimpleMessageStreamProvider> "sms2" Map.empty

   use system = [|typeof<ChatRoomMessage>.Assembly|]   
                |> ActorSystem.createClient config [|"ChatUser"|]
                |> ActorSystem.conect   

   printfn "Enter your user name..."
   let userName = Console.ReadLine();

   printfn "Enter a room which you'd like to join..."
   let roomName = Console.ReadLine();

   let t = startChatClient system userName roomName 
   t.Wait()

   Console.ReadLine() |> ignore  
   0 // return an integer exit code

