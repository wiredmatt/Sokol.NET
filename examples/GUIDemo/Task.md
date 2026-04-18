
## TASK
Please create a Cross Platform self contained C# GUI framework based upon Sokol.NET
The rendering of the GUI will be done by using [NanoVG](../../src/sokol/NanoVG.cs) which is integral part of Sokol.NET
The framework will be modular and easy to extend
The GUI will be created either Programmatically or  via XML (like in Avalonia UI A cross-platform UI framework built on .NET)
The  GUI will adhere to the C# programing standards , it can use the C# 14 Features (.NET 10).
In the future this framework will be a part of a greater Game Engine framwework that will be nuilt on top of Sokol.NET .


## GUI References
- [NanoGUI ](./References/nanogui/) is a minimalistic cross-platform widget library for OpenGL 3+, GLES 2/3, and Metal it is using [NanoVG](../../src/sokol/NanoVG.cs) to draw its primitives.

- [Myra](../../References/Myra/) UI Library for MonoGame, FNA and Stride

- [Avalonia UI](https://github.com/avaloniaui/avalonia) is a cross-platform UI framework for dotnet, providing a flexible styling system and supporting a wide range of platforms such as Windows, macOS, Linux, iOS, Android and WebAssembly.

- [Imgui in Sokol](../../src/imgui) , the Imgui C# bindings in Sokol.NET


## Sokol.NET example references
- [NanoVG Demo](../NanoVGDemo/) A Demo example that shows how to use NanoVG which is an integral part of Sokol.NET  inside an Sokol.NET application , it uses [asynchronous FileSystem](../NanoVGDemo/Source/FileSystem.cs) to load its assets
- [FontStash demo](../fontstash/)
- [cimgui demo](../cimgui) , CImgui demo . CImgui is an integral part of Sokol.NET
- [Another cimgui demo](../imgui_usercallback)
-  [CGltfViewer](../CGltfViewer/) shows. how to utilize Textures in Sokol.NET by using the [Texture](../CGltfViewer/Source/Texture.cs) class and the [Texture cache](../CGltfViewer/Source/TextureCache.cs)
- [Sokol FileSystem demo](../FileSystemTest/) shows how to use File API's in Sokol , I don;t think that is needed in the current implementatio


## Implmentation notes 
- The GUI framework must be modular , every class should be in its own file .
- The GUI framework should be skinnable ,Skinnable GUI frameworks enable dynamic theme changes (colors, fonts, images) and visual customization, often achieved by separating UI logic from visual styling. 
- The GUI should support Direct Approach (Code-Behind) , Smart UI ,It should be possible utilize it Programmatically or via XML , the implementation should start with this because it is the most simple one.
- The GUI should support Model-View-Controller (MVC)
  - The controller handles incoming requests and puts any data the client needs into a component called a model.
  - When the controller's work is done, the model is passed to a view component for rendering.
  - it Programmatically or via XML
The rendered result is sent back to the client.
- The GUI should support MVVM (Model-View-ViewModel) . 
 MVVM is an architectural design pattern that separates GUI development (View) from business/back-end logic (Model) using an intermediary component (ViewModel). It promotes clean code, easy maintenance, and testing, often using two-way data binding to connect View and ViewModel, making the View independent of specific model platforms.
 It should be possible utilize it Programmatically or via XML like in Avalonia UI
- The [shaders](./shaders/) folder will contain the glsl shaders of the demo , inorder to use these shaders they have to be first compiled to C# by using the command 'dotnet build GUIDemo.csproj -t:CompileShaders'.
Once they compiled to C# the entire demo can be compiled by using the command 'dotnet build GUIDemo.csproj'.
- The [Assets](./Assets/) will contain all the assets of this demo
- The GUI framework should be self-contained, all its files should be created in a common folder that can be later used by future Sokol.NET applications , the common folder should be in [GUI folder](../../src/GUI/)
- [GUIDemo-app.cs](./Source/GUIDemo-app.cs) is the main entry point of this demo
- Please create an detaild Implementation.md document with all the steps that are required to create this GUI framework and this demo , every completed step must be marked as complete 




