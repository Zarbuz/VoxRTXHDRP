# VoxToVFX

## Requirements

- Unity 2021.2
- VFX Graph
- HDRP
- Enable Eperimental Operators/Blocks (Edit > Preferences > Visual Effects)

## What is VoxToVFX ? 

VoxToVFX allows you to import a MagicaVoxel project into Unity using the new VFX Graph.
No mesh is created, so the import process for huge world is very quick ! All voxels are particles rendered on the GPU.
It support also world regions of MagicaVoxel so you can import world bigger than 126^3.

## How to use it ? 

You need to convert first your vox file at the runtime. This conversion step is very quick and it convert your files into different chunks with 3 levels of details. 
Once your model is converted, a .zip file is created: it contains all the chunks data. 

![](img/img21.png)

Then you can import the data. 

![](img/img31.png)

## Custom version of Visual Effect Graph 12.1.7

I made a fork of the Visual Effect Graph to be able to use more than 16 contexts since it's not possible to use per-particle sub-mesh mask. 
The modification is in the file "com.unity.visualeffectgraph@12.1.7\Editor\Data\VFXData.cs" line 400 :
`` if (contextCount > 16)
                throw new InvalidOperationException(string.Format("Too many contexts that use particle data {0} > 16", contextCount));``
				
Change 16 to 32

## TODO

- Add support for cloud material
- Add support for realtime raytracing (will need Unity 2022.2 in the future)

