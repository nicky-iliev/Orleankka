namespace Http

open Orleankka
open Orleankka.Playground
open Controller

open System
open System.Web.Http
open System.Web.Http.Dispatcher
open System.Web.Http.Controllers
open System.Net.Http
open System.Reflection
open Newtonsoft.Json

type CompositionRoot(router) =   
   interface IHttpControllerActivator with

      member this.Create(request:HttpRequestMessage, controllerDescriptor:HttpControllerDescriptor, controllerType:Type) = 
         if controllerType = typedefof<ActorController>
            then new ActorController(router) :> IHttpController
         else null


type Global() =
   inherit System.Web.HttpApplication() 

   static member RegisterWebApi(config: HttpConfiguration) =      

      let system = ActorSystem.Configure()
                              .Playground()
                              .Register(Assembly.GetExecutingAssembly())
                              .Done()

      let testActor = system.ActorOf<Actors.TestActor>("http_test")

      // configure actor routing
      let router = [(testActor, typedefof<Actors.HelloMessage>)]
                   |> Seq.map ActorRouter.mapToPath
                   |> ActorRouter.create JsonConvert.DeserializeObject

      // configure controller activator
      config.Services.Replace(typedefof<IHttpControllerActivator>, CompositionRoot(router))

      config.MapHttpAttributeRoutes()
      
      // configure serialization for json     
      let jsonFormatter = config.Formatters.JsonFormatter
      config.Formatters.Clear()
      config.Formatters.Add(jsonFormatter)
      config.Formatters.JsonFormatter.SerializerSettings.ContractResolver <- Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()

    member x.Application_Start() =
        GlobalConfiguration.Configure(Action<_>(Global.RegisterWebApi))