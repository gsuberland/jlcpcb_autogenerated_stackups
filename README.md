# JLCPCB Multi-Layer Stackup Files for Altium & KiCAD

This repository contains auto-generated stackup files for JLCPCB's multi-layer PCBs, plus the LinqPad (C#) script that was used to generate them. It also contains the raw API JSON responses from JLC's website, and a normalised JSON format that you can use to generate your own stackups for other EDA tools without needing to touch my code.

Stackups are generated for all supported board thicknesses, outer copper weights, inner copper weights, and stackup variations, for layer counts from 4 to 32. As of 2025-05-09 this produces 562 separate stackup files.

## Warning

**These stackup files were generated automatically from the stackup specs on JLCPCB's website. They have not been manually tested for validity or accuracy.** There is absolutely no guarantee that the stackups are correct. JLCPCB's data often contains typos and errors, and, while effort has been put in to correct for these, there can be no guarantee of overall correctness. If you are doing anything that relies on accurate stackup information, you must verify that the stackup information is correct.

## Organisation

The Altium stackup files are in the `altium_stackups` directory. These are XML stackups (*.stackupx files) which can be loaded from the File -> Load Stackup From File menu in Altium's Layer Stack Manager.

The KiCAD stackup files are in the `kicad_stackups` directory. These are empty PCBs (.kicad_pcb files) which can be loaded by going to File -> Board Setup -> Import settings from another board, selecting the PCB file, checking "Board layers and physical stackup", then clicking Import Settings.

The raw JSON data returned by JLCPCB's impedance template API is stored in the `raw_json` directory.

Normalised JSON data is included in the `normalised_json` directory. These JSON files are the result of translating JLC's API data into a cleaner form describing the copper and dielectric layers. These may be useful if you want to write your own scripts to make stackup files for other EDA tools. The format should be self-evident.

All files are named in the following way:

```
jlcpcb_[layerCount]L_[boardThickness]mm_outer[outerWeight]oz_inner[innerWeight]oz_[templateName]
```

where `layerCount` is the number of copper layers, `boardThickness` is the nominal PCB thickness that you would enter in the order page, `outerWeight` is the outer layer copper weight (e.g. 0.5, 1.0, 2.0) in ounces, `innerWeight` is the inner layer copper weight in ounces, and `templateName` is the JLCPCB stackup template name (e.g. `JLC04161H-7628` for the default 4L stackup). Default stackups, referred to as "No requirement" by JLC's order page, have a template name of `NOREQ`.

For example, an Altium stackup for a 4L board with 1.6mm thickness, 1oz outer copper, 0.5oz inner copper, and the JLC0416H-7628 stackup would be called:

```
jlcpcb_4L_1.6mm_outer1oz_inner0.5oz_JLC04161H-7628.stackupx
```

## Included specifications

### Copper layers

Copper layers include the following specifications:

- Weight of the copper in oz (Altium only)
- Thickness of the copper
- Via plating thickness of 0.018mm (Altium only)
- Copper resistivity of 17.24nΩ∙m (Altium only; is based on resistivity of annealed copper)
- A Modified Hammerstad roughness profile set to SR=1μm, RF=2 (Altium only)

#### Copper weight and thickness

Altium generally maps copper weights in ounces to an equivalent nominal thickness, e.g. 1/2oz = 0.0175mm. However, JLCPCB's data provides more accurate thickness information based on final copper thickness after accounting for copper loss during the production process. As such, the inner layers for 4-8L boards are specified as having a thickness of 0.0152mm and a weight of 1/2oz. Altium will honour this, but if you touch the copper weight cell in the layer stack manager it will reset the thickness to the nominal value, so be careful if you're editing things.

#### Roughness profile

A [conductor surface roughness profile](https://www.altium.com/documentation/altium-designer/interactively-routing-controlled-impedance-pcb#conductor-surface-roughness) is used to more accurately model trace impedance for high-speed designs, typically becoming relevant when operating above 10Gbps. Neither JLCPCB nor their material providers publish copper roughness information. The roughness profile included with these stackups is a reasonable default based on a review of the literature ([Shlepnev & Nwachukwu, 2012](https://www.simberian.com/AppNotes/14-WA2_Shlepnev_Nwachukwu_DesignCon2012_final.pdf) and [Shlepnev, 2017](https://www.simberian.com/AppNotes/Unified_RCC_EPEPS2017_rev2.pdf)). If you are doing challenging high-speed designs where absolute characteristic impedance accuracy is required, I recommend ordering test boards and validating your impedance profiles.

### Dielectric layers

Dielectric layers include the following specifications:

- Type (Prepreg / Core)
- Material manufacturer and product name
- Construction (e.g. 7628)
- Thickness
- Resin content (Altium only)
- Dielectric constant
- Loss tangent
- Glass transition temperature (Altium only)
- Rated frequency (Altium only)

Note: KiCAD does not properly support multiple dielectric layers between copper layers (see [issue #1](https://github.com/gsuberland/jlcpcb_autogenerated_stackups/issues/1)). As of the 2025-03-10 update, sequential dielectric layers are combined together into one dielectric layer for KiCAD outputs and the construction name is suffixed with a count (e.g. "7628 x2" for two 7628 layers).

#### Material type

JLCPCB's impedance profiles for <10L designs assume NP-155F dielectric material. The stackup files generated by this script make the same assumption. However, for 4L and 6L boards, the quote page defaults to "FR4-Standard TG 135-140" instead of "FR-4 TG155". If you're building impedance controlled boards, make sure to select "FR-4 TG155" as the material when ordering. JLC do not state which material is used for TG135-140 boards.

Note that JLC refers to the S1000-2M material as "TG170", but its material datasheet defines it as having a Tg of 180°C, so I have specified 180°C for those dielectrics.

### Solder mask

The solder mask layers assume the following specs:

- Thickness of 0.01524mm (0.6mil)
- Dielectric constant of 3.8
- No specified loss tangent

## Using the script

The script should be pretty much point-and-shoot. Load it up in [LinqPad](https://www.linqpad.net/) 7.0 or newer (6.x should also work), edit the `OutputPath` variable to point at the directory you want the output files to be placed in, and click run. The query has no NuGet dependencies.

Each layer / thickness / copper weight combination will be printed when fetching. Any fixups made to known errors in the JLC data will be accompanied by a fixup message in the output. Don't worry about the script printing "Failed." after a lot of the fetch attempts - the script tries every combination even though many of them are not valid.

If you're using an operating system other than Windows, you can install dotnet and convert the code in the LinqPad script to a small console program, then build it. It should work out of the box as I did not make any Windows-specific assumptions in the code.

### How do I...

It is inevitable that JLC will add more capabilities and change their stackup data in future. If you find that you need to update stuff, here's a quick guide to the most likely changes you will need to make:

#### Update the JSON structure for JLC's API

The classes used for communicating with JLC's API are defined at the bottom of the script. They're all prefixed with `JLC` to make it clear that they're part of the API interface.

The `JLCStackupRequest` type is the POST data sent to the API. The `JLCStackupResponse` type is the root type for the data returned from the API.

#### Update the available board thicknesses

Available board thicknesses for each layer count are defined in the `GetValidThicknesses` function.

#### Add another layer count

If JLC supports more than 32 layers in future, change the `GetValidLayerCounts` function to include them, and update the `GetValidThicknesses` function to provide the thicknesses for those counts.

#### Add more copper weights

Right now the copper weights are fixed at 1oz, 2oz for outer weight, 0.5oz, 1.0oz, 2.0oz for inner weight. If JLC add new weights beyond this, update the `GetValidOuterCopperWeights` and `GetValidInnerCopperWeights` functions.

#### Update the core or prepreg materials

Each known dielectric material is defined by a `DielectricMaterial` object. These are defined in the `GetKnownPrepregs` and `GetKnownCores` functions. I took the material specs from [here](https://jlcpcb.com/help/article/User-Guide-to-the-JLCPCB-Impedance-Calculator), along with the NP-155F and S1000-2M datasheets.

## Data sources

The primary data source was JLCPCB's web API for impedance templates.

Additional material specifications were sourced from the following locations:

- [JLCPCB Impedance Calculator User Guide](https://jlcpcb.com/help/article/User-Guide-to-the-JLCPCB-Impedance-Calculator)
- [JLCPCB PCB Capabilities](https://jlcpcb.com/capabilities/pcb-capabilities)
- [Nan Ya Plastics NP-155F Datasheet](https://cclqc.npc.com.tw/cclfile/pdt/Datasheet_NP-155F_1728977944054.pdf?v=16750)
- [SYTECH S1000-2M Datasheet](http://syst.com.cn/uploadfiles/2019/03/20190301102556156.pdf)

## License

The LinqPad script is released under MIT license. The license text can be found within the script file.

The stackup files in the `altium_stackups` and `kicad_stackups` directories are released into the public domain with no rights reserved.

The normalised JSON files in the `normalised_json` directory are released into the public domain with no rights reserved.

The raw JSON files in the `raw_json` directory are the property of JLCPCB and are included here for reference only.

