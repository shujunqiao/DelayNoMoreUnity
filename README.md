# Why not just transpile Golang to C#
There was previous effort into a direct transpiling from Golang to C# for the [shared dynamics part of DelayNoMore v1.0.12](https://github.com/genxium/DelayNoMore/tree/v1.0.12-dd/jsexport). 

It was experimented using [go2cs](https://github.com/GridProtectionAlliance/go2cs), and is halted due to the untolerable buggy handling of the following issues by `go2cs`.
- When a `Go struct` has a pointer field, e.g. https://github.com/genxium/DelayNoMore/blob/v1.0.12-dd/resolv_tailored/collision.go#L13
- By the time of testing, its own [go-src-converted](https://github.com/GridProtectionAlliance/go2cs/tree/master/src/go-src-converted) is not fully compilable by `.NET 6.0/7.0 SDK`.
- By the time of testing, its own [golib of gocore](https://github.com/GridProtectionAlliance/go2cs/tree/master/src/gocore/golib) is compilable into a `golib.lib/dll` file, but when referenced by a transpiled `resolv` C# project (transpiled by the compiled `go2cs` binary in same codebase as the `golib`), many types are not found or not correctly formatted.

There're still 2 planned routes for moving on when `go2cs` becomes more usable. 
```
1. Golang -(compile directly)-> game_dynamic_export.lib/dll: very possibly won't work even on Windows, because this lib relies on Golang's "garbage collector" to work for heap management, and even if it's bundled in the lib it'd be very inefficient to run together with .NET runtime

2. Golang -(go2cs)-> C# v10 source code -(compile by .NET 6.0 SDK but targets .NET 4.0 runtime)-> game_dynamic_export.lib/dll: possibly can work in Unity runtime because this lib knows how to cooperate with .NET runtime garbage collector
```

# Why not just transpile Golang to C++ and use Unreal instead
By the time of starting this project, I'm much more familiar with C++ than C#, hence this is really a considered option but there're a few serious reasons that stopped it from happening.
- Unreal Engine IDE is a resource monster, my laptop just couldn't run it smoothly. Unfortunately this is the major reason T_T
- Transpiling Golang to C++ is non-trivial and the difficulties are very similar to that of `Golang to C#`
- What's more, as there's no default `auto garbage collection` mechanism for C++, whenever the __transpiled version of any heap RAM allocating method in [DelayNoMore/jsexport/main.go](https://github.com/genxium/DelayNoMore/blob/v1.0.12-dd/jsexport/main.go)__ is invoked, the allocated instance must be 
    - either manually deallocated later, e.g. [what's already done for DelayNoMore frontend UDP session management](https://github.com/genxium/DelayNoMore/blob/v1.0.12-dd/frontend/build-templates/jsb-link/frameworks/runtime-src/Classes/udp_session.cpp), or
    - bound to framework specific gc system, e.g. [UObject of UE4](https://docs.unrealengine.com/4.27/en-US/ProgrammingAndScripting/ProgrammingWithCPP/UnrealArchitecture/Objects/Optimizations/) on frontend & [Boost Smart Pointer](https://www.boost.org/doc/libs/1_55_0/libs/smart_ptr/smart_ptr.htm) on backend    

In short, it's all feasible but too expensive for me.

# What to expect compared with CocosCreator version 
Now that I've decided to rewrite the whole project in `C#` (i.e. both backend & frontend) to favor the use of Unity3D on frontend, there're some improvements planned to be made as using a single language removes many inconveniences previously encountered by `Golang backend + JavaScript/C++ frontend`.
   
- On backend, deprecate the use of Redis for just keeping captchas like [BuildingAndCraftingAndTowerDefenseGame](https://github.com/genxium/BuildingAndCraftingAndTowerDefenseGame/tree/redis-deprecated).
- On backend, [.NET framework supports WebSocket.SendAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.sendasync?view=net-7.0) which can be the ideal way of implementing [Room.downsyncToAllPlayers](https://github.com/genxium/DelayNoMore/blob/v1.0.12-dd/battle_srv/models/room.go#L1504). Kindly note that not all .NET versions support the same set of APIs, on backend I'd use `.NET 7.0` which is [not supported by Unity yet](https://docs.unity3d.com/Manual/CSharpCompiler.html). 
- For the shared dynamics and protobuf autogen models, there could be a chance to eliminate the duplication of [serialization models](https://github.com/genxium/DelayNoMore/blob/v1.0.12-dd/battle_srv/protos/room_downsync_frame.pb.go) and [dynamics models](https://github.com/genxium/DelayNoMore/blob/v1.0.12-dd/jsexport/battle/room_downsync_frame.go).  

However, there're also challenges to keep in mind even if both backend & frontend are using `C#`.
- Unity supports only a subset of [C# 9.0 w/ Roslyn compiler as of version 2021.3](https://docs.unity3d.com/Manual/CSharpCompiler.html)
- Unity runs in [.NET 4.x equivalent runtime as of version 2019.1](https://docs.unity3d.com/2019.1/Documentation/Manual/ScriptingRuntimeUpgrade.html), but the [runtime .NET equivalence is not explicitly specified as of version 2021.3 -- and .NET 4.x would be a safe assumption](https://docs.unity3d.com/Manual/dotnetProfileLimitations.html).

Language level and runtime version are relatively low compared to what's available for the backend, thus I'd start writing the shared dynamics on frontend for better compatibilty as well as visual testability. 
