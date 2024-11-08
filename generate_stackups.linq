<Query Kind="Program">
  <Namespace>System.Text.Json</Namespace>
  <Namespace>System.Text.Json.Serialization</Namespace>
  <Namespace>System.Net</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
  <Namespace>System.Net.Http.Headers</Namespace>
</Query>

/*

JLCPCB Stackup Generator for Altium & KiCAD
Generates stackup templates using JLC's impedance template API.
Outputs normalised JSON, Altium XML stackups, and KiCAD board templates.

https://github.com/gsuberland/jlcpcb_altium_stackups
Written by Graham Sutherland - https://chaos.social/@gsuberland

This is a fully automated process and the end result is NOT guaranteed to be correct.
Please check before you do anything where stackup is critical.

This script is released under MIT license.

---

Copyright 2024 Graham Sutherland

Permission is hereby granted, free of charge, to any person obtaining a copy of this 
software and associated documentation files (the "Software"), to deal in the Software 
without restriction, including without limitation the rights to use, copy, modify, 
merge, publish, distribute, sublicense, and/or sell copies of the Software, and to 
permit persons to whom the Software is furnished to do so, subject to the following 
conditions:

The above copyright notice and this permission notice shall be included in all copies 
or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, 
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A 
PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT 
HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE 
OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

*/

// directory where you want the output files to be placed
const string OutputPath = @"N:\JLCStackups\";
// subdirectory for the raw JSON dumps (straight from the API)
const string RawJsonSubdir = @"raw_json";
// subdirectory for the normalised JSON (after processing into layer data)
const string NormalisedJsonSubdir = @"normalised_json";
// subdirectory for Altium stackup files
const string AltiumStackupSubdir = @"altium_stackups";
// subdirectory for KiCAD stackup files
const string KicadStackupSubdir = @"kicad_stackups";
// target URL for the stackup service endpoint
const string StackupUrl = @"https://cart.jlcpcb.com/api/overseas-shop-cart/v1/shoppingCart/getImpedanceTemplateSettings";
// max requests per minute
const double RateLimit = 120;

async Task Main()
{
	// create output directories if they don't already exist
	if (!Directory.Exists(OutputPath))
	{
		Directory.CreateDirectory(OutputPath);
	}
	string RawJsonSubdirPath = Path.Combine(OutputPath, RawJsonSubdir);
	if (!Directory.Exists(RawJsonSubdirPath))
	{
		Directory.CreateDirectory(RawJsonSubdirPath);
	}
	string NormalisedJsonSubdirPath = Path.Combine(OutputPath, NormalisedJsonSubdir);
	if (!Directory.Exists(NormalisedJsonSubdirPath))
	{
		Directory.CreateDirectory(NormalisedJsonSubdirPath);
	}
	string AltiumStackupSubdirPath = Path.Combine(OutputPath, AltiumStackupSubdir);
	if (!Directory.Exists(AltiumStackupSubdirPath))
	{
		Directory.CreateDirectory(AltiumStackupSubdirPath);
	}
	string KicadStackupSubdirPath = Path.Combine(OutputPath, KicadStackupSubdir);
	if (!Directory.Exists(KicadStackupSubdirPath))
	{
		Directory.CreateDirectory(KicadStackupSubdirPath);
	}

	var jsoIndent = new JsonSerializerOptions { WriteIndented = true };
	var utf8noBOM = new UTF8Encoding(false);
	
	var stopwatch = new Stopwatch();
	stopwatch.Start();
	foreach (int layerCount in GetValidLayerCounts())
	{
		foreach (double boardThickness in GetValidThicknesses(layerCount))
		{
			foreach (double outerWeight in GetValidOuterCopperWeights(layerCount))
			{
				foreach (double innerWeight in GetValidInnerCopperWeights(layerCount))
				{
					while (stopwatch.ElapsedMilliseconds < (60000.0 / RateLimit))
					{
						Thread.Sleep(100);
					}
					Console.WriteLine($"Fetching {layerCount}L {boardThickness}mm {outerWeight}oz/{innerWeight}oz");
					var stackupResponse = await FetchStackups(layerCount, outerWeight, innerWeight, boardThickness);
					if (stackupResponse?.Templates == null)
					{
						Console.WriteLine("Failed.");
						continue;
					}
					foreach (JLCStackupTemplate template in stackupResponse.Templates)
					{
						if (!template.Enabled)
						{
							Console.WriteLine($"Warning: template {template.TemplateName} is marked as disabled; skipping.");
							continue;
						}
						var stackupName = template.Default ? "NOREQ" : template.TemplateName;
						string fileNameFormat = $"jlcpcb_{template.LayerCount}L_{template.BoardThickness}mm_outer{outerWeight}oz_inner{innerWeight}oz_{stackupName}";
						var rawJsonOutputPath = Path.Combine(RawJsonSubdirPath, fileNameFormat + ".json");
						var rawJsonStr = JsonSerializer.Serialize<JLCStackupTemplate>(template, jsoIndent);
						await File.WriteAllTextAsync(rawJsonOutputPath, rawJsonStr, utf8noBOM);
						
						var stackupData = TranslateStackupData(template);
						if (stackupData == null)
						{
							Console.WriteLine($"Warning: could not translate {fileNameFormat}.json (no laminations?)");
							continue;
						}
						var normalisedJsonOutputPath = Path.Combine(NormalisedJsonSubdirPath, fileNameFormat + ".json");
						
						var normalisedJsonStr = JsonSerializer.Serialize<List<BaseLayerData>>(stackupData, jsoIndent);
						await File.WriteAllTextAsync(normalisedJsonOutputPath, normalisedJsonStr, utf8noBOM);

						var altiumXmlStackup = BuildAltiumStackup(stackupData, template);
						var altiumXmlOutputPath = Path.Combine(AltiumStackupSubdirPath, fileNameFormat + ".stackupx");
						await File.WriteAllTextAsync(altiumXmlOutputPath, altiumXmlStackup, utf8noBOM);

						var kicadBoardTemplate = BuildKicadStackup(stackupData, template);
						var kicadBoardOutputPath = Path.Combine(KicadStackupSubdirPath, fileNameFormat + ".kicad_pcb");
						await File.WriteAllTextAsync(kicadBoardOutputPath, kicadBoardTemplate, utf8noBOM);
						var kicadProject = GetKicadProjectFile(fileNameFormat + ".kicad_pro");
						var kicadProjectOutputPath = Path.Combine(KicadStackupSubdirPath, fileNameFormat + ".kicad_pro");
						await File.WriteAllTextAsync(kicadProjectOutputPath, kicadProject, utf8noBOM);
					}
					stopwatch.Restart();
				}
			}
		}
	}
}

List<BaseLayerData>? TranslateStackupData(JLCStackupTemplate template)
{
	if ((template.Laminations?.Length ?? 0) == 0)
	{
		return null;
	}
	int copperLayerNumber = 0;
	int dielectricLayerNumber = 0;
	int laminationIndex = 0;
	var layers = new List<BaseLayerData>();
	foreach (var lamination in template.Laminations.OrderBy(lam => lam.Sort))
	{
		if (lamination.LaminationType == JLCLaminationType.Line)
		{
			// copper foil

			var lineData = lamination.GetLineContent();
			if (lineData.MaterialType != "Copper")
			{
				Console.WriteLine($"Warning: lamination {laminationIndex} has material type \"{lineData.MaterialType}\" but \"Copper\" was expected. Skipping.");
				continue;
			}
			copperLayerNumber++;

			var thicknessStr = lineData.Thickness;
			if (thicknessStr == "" && template.TemplateName == "JLC061611-1080B")
			{
				Console.WriteLine($"Fixup: JLC061611-1080B has blank copper thickness, replacing with 0.03mm");
				thicknessStr = "0.03mm";
			}
			var thickness = ParseThickness(thicknessStr) ?? throw new InvalidDataException($"Could not parse thickness value \"{thicknessStr}\".");

			var weight = (copperLayerNumber == 1 || copperLayerNumber == template.LayerCount) ? template.OuterCopperWeight : template.InnerCopperWeight;
			
			var copperLayer = new CopperLayerData
			{
				Thickness = thickness,
				Weight = weight
			};
			layers.Add(copperLayer);
		}

		if (lamination.LaminationType == JLCLaminationType.Prepreg)
		{
			// prepreg

			var prepregData = lamination.GetPrepregContent();
			if (prepregData.PrepregLayer != "Prepreg" && prepregData.PrepregLayer != "Core")
			{
				Console.WriteLine($"Warning: lamination {laminationIndex} has layer name \"{prepregData.PrepregLayer}\" but \"Prepreg\" was expected. Skipping.");
				continue;
			}
			dielectricLayerNumber++;

			var thicknessStr = prepregData.Thickness;
			if (thicknessStr == "" && template.TemplateName == "JLC16161H-2313")
			{
				Console.WriteLine($"Fixup: JLC16161H-2313 has blank prepreg thickness, replacing with 0.0888mm");
				thicknessStr = "0.0888mm";
			}
			var thickness = ParseThickness(thicknessStr) ?? throw new InvalidDataException($"Could not parse thickness value \"{thicknessStr}\".");
			
			DielectricLayerData dielectricLayer;
			string? materialConstructionRaw = prepregData.MaterialType?.Trim();
			string? material = null;
			int materialCount = 1;
			bool isCore = false;
			// sometimes they have a prepreg layer that actually describes a core. in such a case the material name will be empty, "Core", or "SY1000-2M".
			if (!string.IsNullOrEmpty(materialConstructionRaw))
			{
				// the material is usually spec'd as material*count, e.g. 7628*1, so just take the left side
				var materialParts = prepregData.MaterialType?.Split('*');
				material = materialParts?.First().Trim();
				string? materialCountStr = materialParts?.Length > 1 ? materialParts[1].Trim() : null;
				// JLC made a typo on the JLC04201H-1080 stackup (4L 2.0mm 1oz/0.5oz) so the count says ! instead of 1
				if (materialCountStr == "!")
				{
					Console.WriteLine($"Fixup: replacing material count '!' with material count '1'");
					materialCountStr = "1";
				}
				if (materialParts?.Length > 2 || (materialCountStr != null && !int.TryParse(materialCountStr, out materialCount)))
				{
					throw new InvalidDataException($"Lamination {laminationIndex} has unrecognised material spec \"{prepregData.MaterialType}\"");
				}
				if (materialCount > 1)
				{
					throw new NotImplementedException($"Lamination {laminationIndex} uses stacked construction \"{prepregData.MaterialType}\"; not implemented yet!");
				}
			}
			if (material == null || material == "Core" || material == "S1000-2M")
			{
				// prepreg layer actually describes a core
				isCore = true;
			}

			var dielectric = GetBestDielectricMatch(isCore, material, thickness, template.LayerCount);
			if (dielectric == null)
			{
				throw new InvalidDataException($"Could not find dielectric material match for {(isCore ? "core" : (material + " prepreg"))} with thickness \"{thickness}\" and board layer count {template.LayerCount}.");
			}

			dielectricLayer = new DielectricLayerData
			{
				Material = dielectric,
				Thickness = thickness
			};
			
			layers.Add(dielectricLayer);
		}


		if (lamination.LaminationType == JLCLaminationType.Core)
		{
			// core

			var coreData = lamination.GetCoreContent();

			foreach (var coreEntry in coreData.Entries)
			{
				if (coreEntry.MaterialType == "Copper")
				{
					// copper foil on core
					
					copperLayerNumber++;

					var thicknessStr = coreEntry.Thickness;
					var thickness = ParseThickness(thicknessStr) ?? throw new InvalidDataException($"Could not parse thickness value \"{thicknessStr}\".");

					var weight = template.InnerCopperWeight; // are cores *always* internal?
					
					var copperLayer = new CopperLayerData
					{
						Thickness = thickness,
						Weight = weight
					};
					layers.Add(copperLayer);
				}
				
				if (coreEntry.MaterialType == "Core")
				{
					var thicknessStr = coreEntry.Thickness;
					var thickness = ParseThickness(thicknessStr) ?? throw new InvalidDataException($"Could not parse thickness value \"{thicknessStr}\".");
					var material = "Core";
					var dielectric = GetBestDielectricMatch(true, material, thickness, template.LayerCount);
					if (dielectric == null)
					{
						throw new InvalidDataException($"Could not find dielectric material match for core with thickness \"{thickness}\" and board layer count {template.LayerCount}.");
					}
					var dielectricLayer = new DielectricLayerData
					{
						Material = dielectric,
						Thickness = thickness
					};
					layers.Add(dielectricLayer);
				}
			}
		}

		laminationIndex++;
	}
	
	return layers;
}

// regex to match thickness values. almost all values are suffixed with "mm" but we have to accept ones without because some (e.g. JLC06161H-1080B) have no suffix
readonly Regex ThicknessRegex = new Regex(@"^(?<thickness>\d+(?:\.\d+))\s*(?:mm)?$");

double? ParseThickness(string? thickness)
{
	if (thickness == null)
	{
		return null;
	}
	var match = ThicknessRegex.Match(thickness);
	if (!match.Success)
	{
		return null;
	}
	double result;
	return double.TryParse(match.Groups["thickness"].ValueSpan, out result) ? result : null;
}

string BuildKicadStackup(List<BaseLayerData> layers, JLCStackupTemplate template)
{
	var pieces = new List<string>();
	int copperLayerNumber = 0;
	int dielectricLayerNumber = 0;
	string topCopperLayerGuid = "";
	string bottomCopperLayerGuid = "";
	foreach (var layer in layers)
	{
		if (layer is CopperLayerData)
		{
			var copperLayer = (CopperLayerData)layer;
			copperLayerNumber++;

			var layerName = MapCopperLayerNumberToKicadLayerName(copperLayerNumber, template.LayerCount);
			var layerPiece = GetKicadBoardStackupCopperLayer(layerName, copperLayer.Thickness);
			pieces.Add(layerPiece);
		}
		else if (layer is DielectricLayerData)
		{
			var dielectricLayer = (DielectricLayerData)layer;
			dielectricLayerNumber++;


			var layerName = $"dielectric {dielectricLayerNumber}";
			var layerPiece = GetKicadBoardStackupDielectricLayer(
				layerName, 
				dielectricLayer.Thickness,
				dielectricLayer.Material.IsCore,
				$"{dielectricLayer.Material.Manufacturer} {dielectricLayer.Material.Model}",
				dielectricLayer.Material.Construction,
				dielectricLayer.Material.DielectricConstant,
				dielectricLayer.Material.LossTangent
			);
			
			pieces.Add(layerPiece);
		}
	}

	var prefix = GetKicadBoardPrefix(template.BoardThickness, template.LayerCount - 2);
	var suffix = GetKicadBoardSuffix();

	pieces.Insert(0, prefix);
	pieces.Add(suffix);

	return string.Join("", pieces);
}

string MapCopperLayerNumberToKicadLayerName(long layerNumber, long layerCount)
{
	if (layerNumber == 1)
		return "F.Cu";
	if (layerNumber == layerCount)
		return "B.Cu";

	return $"In{layerNumber - 1}.Cu";
}

string GetKicadProjectFile(string projectFileName)
{
	string template = @"{""board"":{""3dviewports"":[],""design_settings"":{""defaults"":{},""diff_pair_dimensions"":[],""drc_exclusions"":[],""meta"":{""version"":2},""rule_severities"":{},""rules"":{},""teardrop_options"":[{}],""teardrop_parameters"":[],""track_widths"":[],""tuning_pattern_settings"":{},""via_dimensions"":[]},""ipc2581"":{""dist"":"""",""distpn"":"""",""internal_id"":"""",""mfg"":"""",""mpn"":""""},""layer_presets"":[],""viewports"":[]},""boards"":[],""cvpcb"":{""equivalence_files"":[]},""libraries"":{""pinned_footprint_libs"":[],""pinned_symbol_libs"":[]},""meta"":{""filename"":""$FILE_NAME$"",""version"":1},""net_settings"":{""classes"":[],""meta"":{""version"":3},""net_colors"":null,""netclass_assignments"":null,""netclass_patterns"":[]},""pcbnew"":{""last_paths"":{""gencad"":"""",""idf"":"""",""netlist"":"""",""plot"":"""",""pos_files"":"""",""specctra_dsn"":"""",""step"":"""",""svg"":"""",""vrml"":""""},""page_layout_descr_file"":""""},""schematic"":{""legacy_lib_dir"":"""",""legacy_lib_list"":[]},""sheets"":[],""text_variables"":{}}";
	return template.Replace("$FILE_NAME$", projectFileName.Replace("\"", "\\\""));
}

string GetKicadBoardPrefix(double thickness, long internalLayerCount)
{
	// todo: thickness will be wrong (nominal vs. sum of layers) so check if this is ok
	string layerTemplate = "\t\t($NUM$ \"In$NUM$.Cu\" signal)";
	string template = @"(kicad_pcb
	(version 20240108)
	(generator ""pcbnew"")
	(generator_version ""8.0"")
	(general
		(thickness $THICKNESS$)
		(legacy_teardrops no)
	)
	(paper ""A4"")
	(layers
		(0 ""F.Cu"" signal)
$INNER_LAYERS$
		(31 ""B.Cu"" signal)
		(32 ""B.Adhes"" user ""B.Adhesive"")
		(33 ""F.Adhes"" user ""F.Adhesive"")
		(34 ""B.Paste"" user)
		(35 ""F.Paste"" user)
		(36 ""B.SilkS"" user ""B.Silkscreen"")
		(37 ""F.SilkS"" user ""F.Silkscreen"")
		(38 ""B.Mask"" user)
		(39 ""F.Mask"" user)
		(40 ""Dwgs.User"" user ""User.Drawings"")
		(41 ""Cmts.User"" user ""User.Comments"")
		(42 ""Eco1.User"" user ""User.Eco1"")
		(43 ""Eco2.User"" user ""User.Eco2"")
		(44 ""Edge.Cuts"" user)
		(45 ""Margin"" user)
		(46 ""B.CrtYd"" user ""B.Courtyard"")
		(47 ""F.CrtYd"" user ""F.Courtyard"")
		(48 ""B.Fab"" user)
		(49 ""F.Fab"" user)
		(50 ""User.1"" user)
		(51 ""User.2"" user)
		(52 ""User.3"" user)
		(53 ""User.4"" user)
		(54 ""User.5"" user)
		(55 ""User.6"" user)
		(56 ""User.7"" user)
		(57 ""User.8"" user)
		(58 ""User.9"" user)
	)
	(setup
		(stackup
			(layer ""F.SilkS""
				(type ""Top Silk Screen"")
			)
			(layer ""F.Paste""
				(type ""Top Solder Paste"")
			)
			(layer ""F.Mask""
				(type ""Top Solder Mask"")
				(thickness 0.01524)
				(material ""JLCPCB Soldermask"")
				(epsilon_r 3.8)
				(loss_tangent 0)
			)
";
	var layerDefs = new StringBuilder();
	for (int i = 1; i <= internalLayerCount; i++)
	{
		layerDefs.AppendLine(layerTemplate.Replace("$NUM$", i.ToString()));
	}
	return template
		.Replace("$THICKNESS$", thickness.ToString())
		.Replace("$INNER_LAYERS$", layerDefs.ToString().TrimEnd('\r', '\n'));
}

string GetKicadBoardStackupCopperLayer(string layerName, double thickness)
{
	string template = @"			(layer ""$LAYER_NAME$""
				(type ""copper"")
				(thickness $THICKNESS$)
			)
";
	return template
		.Replace("$LAYER_NAME$", layerName)
		.Replace("$THICKNESS$", thickness.ToString());
}

string GetKicadBoardStackupDielectricLayer(string layerName, double thickness, bool isCore, string material, string construction, double dK, double dF)
{
	string template = @"			(layer ""$LAYER_NAME$""
				(type ""$TYPE$"")
				(color ""FR4 natural"")
				(thickness $THICKNESS$)
				(material ""$MATERIAL$ $CONSTRUCTION$"")
				(epsilon_r $DIELECTRIC_CONSTANT$)
				(loss_tangent $LOSS_TANGENT$)
			)
";
	return template
		.Replace("$TYPE$", isCore ? "core" : "prepreg")
		.Replace("$LAYER_NAME$", layerName)
		.Replace("$THICKNESS$", thickness.ToString())
		.Replace("$MATERIAL$", material.Replace("\"", "\\\""))
		.Replace("$CONSTRUCTION$", construction.Replace("\"", "\\\""))
		.Replace("$DIELECTRIC_CONSTANT$", dK.ToString())
		.Replace("$LOSS_TANGENT$", dF.ToString());
}

string GetKicadBoardSuffix()
{
	string template = @"			(layer ""B.Mask""
				(type ""Bottom Solder Mask"")
				(thickness 0.01524)
				(material ""JLCPCB Soldermask"")
				(epsilon_r 3.8)
				(loss_tangent 0)
			)
			(layer ""B.Paste""
				(type ""Bottom Solder Paste"")
			)
			(layer ""B.SilkS""
				(type ""Bottom Silk Screen"")
			)
			(copper_finish ""None"")
			(dielectric_constraints yes)
		)
		(pad_to_mask_clearance 0)
		(allow_soldermask_bridges_in_footprints no)
		(pcbplotparams
			(layerselection 0x00010fc_ffffffff)
			(plot_on_all_layers_selection 0x0000000_00000000)
			(disableapertmacros no)
			(usegerberextensions no)
			(usegerberattributes yes)
			(usegerberadvancedattributes yes)
			(creategerberjobfile yes)
			(dashed_line_dash_ratio 12.000000)
			(dashed_line_gap_ratio 3.000000)
			(svgprecision 4)
			(plotframeref no)
			(viasonmask no)
			(mode 1)
			(useauxorigin no)
			(hpglpennumber 1)
			(hpglpenspeed 20)
			(hpglpendiameter 15.000000)
			(pdf_front_fp_property_popups yes)
			(pdf_back_fp_property_popups yes)
			(dxfpolygonmode yes)
			(dxfimperialunits yes)
			(dxfusepcbnewfont yes)
			(psnegative no)
			(psa4output no)
			(plotreference yes)
			(plotvalue yes)
			(plotfptext yes)
			(plotinvisibletext no)
			(sketchpadsonfab no)
			(subtractmaskfromsilk no)
			(outputformat 1)
			(mirror no)
			(drillshape 1)
			(scaleselection 1)
			(outputdirectory """")
		)
	)
	(net 0 """")
)";
	return template;
}

string BuildAltiumStackup(List<BaseLayerData> layers, JLCStackupTemplate template)
{
	var layersXml = new List<string>();
	int copperLayerNumber = 0;
	int dielectricLayerNumber = 0;
	string topCopperLayerGuid = "";
	string bottomCopperLayerGuid = "";
	foreach (var layer in layers)
	{
		if (layer is CopperLayerData)
		{
			var copperLayer = (CopperLayerData)layer;
			copperLayerNumber++;

			string layerGuid = Guid.NewGuid().ToString();
			if (copperLayerNumber == 1)
			{
				topCopperLayerGuid = layerGuid;
			}
			else if (copperLayerNumber == template.LayerCount)
			{
				bottomCopperLayerGuid = layerGuid;
			}

			var layerName = MapCopperLayerNumberToAltiumLayerName(copperLayerNumber, template.LayerCount);
			PartPlacement placement;
			if (copperLayerNumber == 1)
				placement = PartPlacement.Up;
			else if (copperLayerNumber == template.LayerCount)
				placement = PartPlacement.Down;
			else
				placement = PartPlacement.None;

			var layerXml = GetAltiumStackupXmlCopperLayer(layerName, layerGuid, copperLayer.Weight, copperLayer.Thickness, placement);
			layersXml.Add(layerXml);
		}
		else if (layer is DielectricLayerData)
		{
			var dielectricLayer = (DielectricLayerData)layer;
			dielectricLayerNumber++;

			string layerGuid = Guid.NewGuid().ToString();

			var layerName = $"Dielectric {dielectricLayerNumber}";

			var layerXml = GetAltiumStackupXmlDielectricLayer(
				layerName,
				layerGuid,
				dielectricLayer.Material.Manufacturer,
				dielectricLayer.Material.Model,
				dielectricLayer.Material.Construction,
				dielectricLayer.Material.ResinContent,
				dielectricLayer.Material.DielectricConstant,
				dielectricLayer.Material.LossTangent,
				dielectricLayer.Material.GlassTransitionTemperature,
				dielectricLayer.Thickness,
				dielectricLayer.Material.IsCore
			);
			layersXml.Add(layerXml);
		}
	}

	var layerStackGuid = Guid.NewGuid().ToString();
	var prefix = GetAltiumStackupXmlPrefix(layerStackGuid);
	var suffix = GetAltiumStackupXmlSuffix(template.LayerCount, topCopperLayerGuid, bottomCopperLayerGuid, layerStackGuid);

	layersXml.Insert(0, prefix);
	layersXml.Add(suffix);

	return string.Join("", layersXml);
}

string MapCopperLayerNumberToAltiumLayerName(long layerNumber, long layerCount)
{
	if (layerNumber == 1)
		return "Top Layer";
	if (layerNumber == layerCount)
		return "Bottom Layer";

	return $"Inner Layer {layerNumber - 1}";
}

string GetAltiumStackupXmlDielectricLayer(string layerName, string layerGuid, string manufacturer, string product, string construction, double resinContent, double dielectricConstant, double lossTangent, int glassTransition, double thickness, bool isCore)
{
	string prepregGuid = @"1a79611a-039d-4d40-a204-53c26c50f8b5";
	string coreGuid = @"136c62ef-1fa6-4897-ae71-7e797b632b92";
	string resinTemplate = @"
              <Property Name=""Material.Resin"" Type=""DimValue"" Dimension=""Relative"">$RESIN_CONTENT$</Property>";
	string template = @"
          <Layer Id=""$LAYER_GUID$"" TypeId=""$TYPE_GUID$"" Name=""$LAYER_NAME$"" IsShared=""True"">
            <Properties>
              <Property Name=""Material"" Type=""String"">$PRODUCT$</Property>
              <Property Name=""Material.Constructions"" Type=""String"">$CONSTRUCTION$</Property>$RESIN_LINE$
              <Property Name=""Material.Frequency"" Type=""FrequencyValue"">1GHz</Property>
              <Property Name=""DielectricConstant"" Type=""DimValue"" Dimension=""Dimensionless"">$DIELECTRIC_CONSTANT$</Property>
              <Property Name=""LossTangent"" Type=""DimensionlessValue"">$LOSS_TANGENT$</Property>
              <Property Name=""Material.GlassTransTemp"" Type=""DimValue"" Dimension=""Temperature"">$GLASS_TRANS$C</Property>
              <Property Name=""Material.Manufacturer"" Type=""String"">$MANUFACTURER$</Property>
              <Property Name=""Thickness"" Type=""LengthValue"">$THICKNESS$</Property>
            </Properties>
          </Layer>
";

	return template
		.Replace("$LAYER_GUID$", layerGuid)
		.Replace("$LAYER_NAME$", layerName)
		.Replace("$MANUFACTURER$", manufacturer)
		.Replace("$PRODUCT$", product)
		.Replace("$TYPE_GUID$", isCore ? coreGuid : prepregGuid)
		.Replace("$CONSTRUCTION$", construction)
		.Replace("$RESIN_LINE$", resinContent < double.Epsilon ? "" : resinTemplate)
		.Replace("$RESIN_CONTENT$", $"{resinContent}%")
		.Replace("$DIELECTRIC_CONSTANT$", dielectricConstant.ToString())
		.Replace("$LOSS_TANGENT$", lossTangent.ToString())
		.Replace("$GLASS_TRANS$", glassTransition.ToString())
		.Replace("$THICKNESS$", $"{thickness}mm");
}

string GetAltiumStackupXmlCopperLayer(string layerName, string layerGuid, double weight, double thickness, PartPlacement partPlacement)
{
	const string template = @"
          <Layer Id=""$LAYER_GUID$"" TypeId=""f4eccd87-2cfb-4f37-be50-4f3a272b4d01"" Name=""$LAYER_NAME$"" IsShared=""True"">
            <Properties>
              <Property Name=""Material"" Type=""EntityReference"">e85f4f0a-a124-40fc-8272-9070953eff8f:CF-004</Property>
              <Property Name=""Weight"" Type=""MassValue"">$COPPER_WEIGHT$</Property>
              <Property Name=""Process"" Type=""String"">ED</Property>
              <Property Name=""Material.Manufacturer"" Type=""String"">Altium Designer</Property>
              <Property Name=""Material.Description"" Type=""String"">Copper Foil</Property>
              <Property Name=""Thickness"" Type=""LengthValue"">$THICKNESS$</Property>
              <Property Name=""CopperOrientation"" Type=""Int32"">0</Property>
              <Property Name=""ComponentPlacement"" Type=""Altium.LayerStackManager.LayerSchema.ComponentPlacement, Altium.LayerStackManager.Abstractions, Version=1.0.0.0, Culture=neutral, PublicKeyToken=51600b9dd346ed18"">$PART_PLACEMENT$</Property>
            </Properties>
          </Layer>
";
	
	string placement = partPlacement switch {
		PartPlacement.None => "None",
		PartPlacement.Up => "BodyUp",
		PartPlacement.Down => "BodyDown",
		_ => throw new InvalidDataException()
	};
	
	return template
		.Replace("$LAYER_GUID$", layerGuid)
		.Replace("$LAYER_NAME$", layerName)
		.Replace("$COPPER_WEIGHT$", $"{weight}oz")
		.Replace("$THICKNESS$", $"{thickness}mm")
		.Replace("$PART_PLACEMENT$", placement);
}

string GetAltiumStackupXmlPrefix(string layerStackGuid)
{
	const string template = @"<StackupDocument SerializerVersion=""1.1.0.0"" Version=""2.1.0.0"" Id=""$DOCUMENT_GUID$"" RevisionId=""$REVISION_GUID$"" RevisionDate=""$REVISION_DATE$"" xmlns=""http://altium.com/ns/LayerStackManager"">
  <FeatureSet>
    <Feature Id=""c8939e8a-fd0e-4d52-8860-b7a98f452016"">Standard Stackup</Feature>
    <Feature Id=""e3df2b86-5f1b-49ca-b266-d1ae57f0ba6f"">Impedance Calculator</Feature>
  </FeatureSet>
  <TypeExtensions />
  <Stackup Type=""Standard"" RoughnessType=""MHammerstad"" RoughnessFactorSR=""1um"" RoughnessFactorRF=""2%"" RealisticRatio=""True"" CopperResistance=""17.24nohm"" ViaPlatingThickness=""18um"" AmbientTemperature=""20C"" TemperatureRise=""50C"">
    <Stacks>
      <Stack Id=""$STACK_GUID$"" Name=""Board Layer Stack"" IsSymmetric=""True"" TemplateId=""4f86428c-8079-42f7-936e-755c6ea7c339"">
        <Layers>
          <Layer Id=""$TOP_OVERLAY_LAYER_GUID$"" TypeId=""c7ef040e-8d00-490b-b00c-a7e7823ff174"" Name=""Top Overlay"" IsShared=""True"">
            <Properties />
          </Layer>
          <Layer Id=""$TOP_SOLDER_LAYER_GUID$"" TypeId=""7b384237-13d8-4318-8bcb-accd8d9a51e7"" Name=""Top Solder"" IsShared=""True"">
            <Properties>
              <Property Name=""Thickness"" Type=""DimValue"" Dimension=""Length"">0.01524mm</Property>
              <Property Name=""Material"" Type=""String"">Solder Resist</Property>
              <Property Name=""DielectricConstant"" Type=""DimValue"" Dimension=""Dimensionless"">3.8</Property>
              <Property Name=""CoverlayExpansion"" Type=""LengthValue"">0mm</Property>
            </Properties>
          </Layer>
";

	var documentGuid = Guid.NewGuid().ToString();
	var revisionGuid = Guid.NewGuid().ToString();
	var revisionDate = DateTime.UtcNow.ToString("O");
	var topOverlayGuid = Guid.NewGuid().ToString();
	var topSolderGuid = Guid.NewGuid().ToString();

	return template
		.Replace("$DOCUMENT_GUID$", documentGuid)
		.Replace("$REVISION_GUID$", revisionGuid)
		.Replace("$REVISION_DATE$", revisionDate)
		.Replace("$STACK_GUID$", layerStackGuid)
		.Replace("$TOP_OVERLAY_LAYER_GUID$", topOverlayGuid)
		.Replace("$TOP_SOLDER_LAYER_GUID$", topSolderGuid);
}

string GetAltiumStackupXmlSuffix(long layerCount, string topLayerGuid, string bottomLayerGuid, string layerStackGuid)
{
	const string template = @"
          <Layer Id=""$BOTTOM_SOLDER_GUID$"" TypeId=""7b384237-13d8-4318-8bcb-accd8d9a51e7"" Name=""Bottom Solder"" IsShared=""True"">
            <Properties>
              <Property Name=""Thickness"" Type=""DimValue"" Dimension=""Length"">0.01524mm</Property>
              <Property Name=""Material"" Type=""String"">Solder Resist</Property>
              <Property Name=""DielectricConstant"" Type=""DimValue"" Dimension=""Dimensionless"">3.8</Property>
              <Property Name=""CoverlayExpansion"" Type=""LengthValue"">0mm</Property>
            </Properties>
          </Layer>
          <Layer Id=""$BOTTOM_OVERLAY_GUID$"" TypeId=""c7ef040e-8d00-490b-b00c-a7e7823ff174"" Name=""Bottom Overlay"" IsShared=""True"">
            <Properties />
          </Layer>
        </Layers>
        <ViaSpans>
          <ViaSpan Id=""$VIA_SPAN_GUID$"" AutoName=""Thru 1:$LAYER_COUNT$"" Type=""ThruVia"" StartLayerId=""$TOP_LAYER_GUID$"" StopLayerId=""$BOTTOM_LAYER_GUID$"" />
        </ViaSpans>
        <DrillSpans />
      </Stack>
    </Stacks>
    <ImpedanceProfiles />
    <Branches>
      <Branch Id=""$BRANCH_GUID$"" Name=""Board"" Description="""">
        <Sections>
          <BranchSection Id=""$BRANCH_SECTION_GUID$"" Name=""Branch Section-1"">
            <Stacks>
              <BranchSectionStack Id=""$BRANCH_SECTION_STACK_GUID$"" LayerStackId=""$LAYER_STACK_GUID$"" Description=""Board Layer Stack"" MaterialUsage=""Common"" Source=""Design"" IsLeftIntrusionsLinked=""True"" IntrusionLeftBottom=""0m"" IntrusionLeftTop=""0m"" IsRightIntrusionsLinked=""True"" IntrusionRightBottom=""0m"" IntrusionRightTop=""0m"" />
            </Stacks>
          </BranchSection>
        </Sections>
      </Branch>
    </Branches>
  </Stackup>
</StackupDocument>";

	var bottomSolderGuid = Guid.NewGuid().ToString();
	var bottomOverlayGuid = Guid.NewGuid().ToString();
	var viaSpanGuid = Guid.NewGuid().ToString();
	var branchGuid = Guid.NewGuid().ToString();
	var branchSectionGuid = Guid.NewGuid().ToString();
	var branchSectionStackGuid = Guid.NewGuid().ToString();
	
	return template
		.Replace("$BOTTOM_SOLDER_GUID$", bottomSolderGuid)
		.Replace("$BOTTOM_OVERLAY_GUID$", bottomOverlayGuid)
		.Replace("$VIA_SPAN_GUID$", viaSpanGuid)
		.Replace("$LAYER_COUNT$", layerCount.ToString())
		.Replace("$TOP_LAYER_GUID$", topLayerGuid)
		.Replace("$BOTTOM_LAYER_GUID$", bottomLayerGuid)
		.Replace("$BRANCH_GUID$", branchGuid)
		.Replace("$BRANCH_SECTION_GUID$", branchSectionGuid)
		.Replace("$BRANCH_SECTION_STACK_GUID$", branchSectionStackGuid)
		.Replace("$LAYER_STACK_GUID$", layerStackGuid);
}

List<int> GetValidLayerCounts()
{
	return new List<int> { 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32 };
}

List<double> GetValidThicknesses(int layerCount)
{
	var thicknessMap = new Dictionary<int, List<double>> {
		{ 4, new List<double> { 0.8, 1.0, 1.2, 1.6, 2.0 } },
		{ 6, new List<double> { 0.8, 1.0, 1.2, 1.6, 2.0 } },
		{ 8, new List<double> { 0.8, 1.0, 1.2, 1.6, 2.0 } },
		{ 10, new List<double> { 1.0, 1.2, 1.6, 2.0 } },
		{ 12, new List<double> { 1.2, 1.6, 2.0, 2.5 } },
		{ 14, new List<double> { 1.6, 2.0, 2.5 } },
		{ 16, new List<double> { 1.6, 2.0, 2.5 } },
		{ 18, new List<double> { 2.0, 2.5 } },
		{ 20, new List<double> { 2.0, 2.5 } },
		{ 22, new List<double> { 2.5, 3.0 } },
		{ 24, new List<double> { 2.5, 3.2 } },
		{ 26, new List<double> { 2.5, 3.5 } },
		{ 28, new List<double> { 3.0, 4.0 } },
		{ 30, new List<double> { 3.0, 4.0 } },
		{ 32, new List<double> { 3.0, 3.2, 4.5 } },
	};
	return thicknessMap[layerCount];
}

List<double> GetValidOuterCopperWeights(int layerCount)
{
	// for now just return the same for all counts
	return new List<double> { 1.0, 2.0 };
}

List<double> GetValidInnerCopperWeights(int layerCount)
{
	// for now just return the same for all counts
	return new List<double> { 0.5, 1.0, 2.0 };
}

async Task<JLCStackupResponse?> FetchStackups(int layerCount, double outerCopperWeightOz, double innerCopperWeightOz, double boardThickness)
{
	using (var client = new HttpClient())
	{
		var request = new JLCStackupRequest
		{
			LayerCount = layerCount,
			OuterCopperThickness = outerCopperWeightOz,
			InnerCopperThickness = innerCopperWeightOz,
			BoardThickness = boardThickness
		};
		var requestJson = JsonSerializer.Serialize<JLCStackupRequest>(request);
		var response = await client.PostAsync(StackupUrl, new StringContent(requestJson, MediaTypeHeaderValue.Parse("application/json")));
		if (response?.IsSuccessStatusCode != true)
		{
			Console.WriteLine($"Server returned status code {response?.StatusCode}");
			return null;
		}
		var responseJson = await JsonSerializer.DeserializeAsync<JLCStackupResponse>(await response.Content.ReadAsStreamAsync());
		if ((responseJson?.ResponseCode ?? 0) != 200)
		{
			Console.WriteLine($"The server returned an error code: {responseJson?.ResponseCode} \"{responseJson?.Message}\"");
			return null;
		}
		return responseJson;
	}
}

enum PartPlacement
{
	None,
	Up,
	Down
}

DielectricMaterial? GetBestDielectricMatch(bool core, string? construction, double thickness, long layerCount)
{	
	if (construction == "3311")
	{
		Console.WriteLine("Fixup: replacing construction 3311 with 3313 (likely typo in JLC data)");
		construction = "3313";
	}

	if (core)
	{
		var cores = GetKnownCores();
		// first try to find a core that's a match on layer count, looking for the one that's closest in nominal thickness
		var candidate = cores
			.Where(p => layerCount >= p.ExpectedLayerCount)
			.OrderBy(c => Math.Abs(c.NominalThickness - thickness))
			.FirstOrDefault();
		if (candidate == null)
		{
			Console.WriteLine($"WARNING: Couldn't find a matching dielectric for {construction} core restricted to {layerCount}L; falling back to unrestricted layer count.");
			throw new NotImplementedException("Fallback from layer-restricted to non-layer-restricted not tested; remove this exception only once you have tested that it is the correct behaviour for new JLC data.");
			candidate = cores
				.OrderBy(c => Math.Abs(c.NominalThickness - thickness))
				.FirstOrDefault();
		}
		return candidate;
	}
	else
	{
		if (string.IsNullOrEmpty(construction) || construction == "Core")
		{
			Console.WriteLine($"WARNING: Prepreg with unexpected core-like construction.");
			throw new NotImplementedException("Fallback from prepreg to core dielectric not tested; remove this exception only once you have tested that it is the correct behaviour for new JLC data.");
			return GetBestDielectricMatch(true, construction, thickness, layerCount);
		}
		
		var prepregs = GetKnownPrepregs();
		// first try to find a prepreg that's a match on layer count and construction
		var candidate = prepregs
			.Where(p => layerCount >= p.ExpectedLayerCount)
			.OrderByDescending(c => c.NominalThickness)
			.FirstOrDefault(p => p.Construction == construction);
		if (candidate == null)
		{
			Console.WriteLine($"WARNING: Couldn't find a matching dielectric for {construction} prepreg restricted to {layerCount}L; falling back to unrestricted layer count.");
			// try again but with no layer count restriction (some high layer count stackups mix NYP dielectrics in)
			candidate = prepregs
				.OrderByDescending(c => c.NominalThickness)
				.FirstOrDefault(p => p.Construction == construction);
		}
		return candidate;
	}
}

DielectricMaterial[] GetKnownPrepregs()
{
	// from https://jlcpcb.com/help/article/User-Guide-to-the-JLCPCB-Impedance-Calculator
	// also data from NP-155F and S1000-2M datasheets
	const double MIL = 0.0254;
	const string NYP = "Nan Ya Plastics";
	const string NP155F = "NP-155F";
	const string SYTECH = "SYTECH (Shengyi)";
	const string S1000 = "S1000-2M";
	return new DielectricMaterial[]
	{
		new DielectricMaterial(NYP, NP155F, "7628", 8.6 * MIL, 49, 4.4,  0.02, 150, false),
		new DielectricMaterial(NYP, NP155F, "3313", 4.2 * MIL, 57, 4.1,  0.02, 150, false),
		new DielectricMaterial(NYP, NP155F, "1080", 3.3 * MIL, 67, 3.91, 0.02, 150, false),
		new DielectricMaterial(NYP, NP155F, "2116", 4.9 * MIL, 54, 4.16, 0.02, 150, false),
		new DielectricMaterial(SYTECH, S1000, "106",  1.97 * MIL, 72, 3.92, 0.018, 180, false, 10),
		new DielectricMaterial(SYTECH, S1000, "1080", 3.31 * MIL, 69, 3.99, 0.018, 180, false, 10),
		new DielectricMaterial(SYTECH, S1000, "2313", 4.09 * MIL, 58, 4.31, 0.018, 180, false, 10),
		new DielectricMaterial(SYTECH, S1000, "2116", 5.00 * MIL, 57, 4.29, 0.018, 180, false, 10),
	};
}

DielectricMaterial[] GetKnownCores()
{
	// from https://jlcpcb.com/help/article/User-Guide-to-the-JLCPCB-Impedance-Calculator
	// also data from NP-155F and S1000-2M datasheets
	const string NYP = "Nan Ya Plastics";
	const string NP155F = "NP-155F";
	const string SYTECH = "SYTECH (Shengyi)";
	const string S1000 = "S1000-2M";
	const string Core = "Core";
	return new DielectricMaterial[]
	{
		new DielectricMaterial(NYP, NP155F, Core, 0.08, 0, 3.99, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.10, 0, 4.36, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.13, 0, 4.17, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.15, 0, 4.36, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.20, 0, 4.36, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.25, 0, 4.23, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.30, 0, 4.41, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.35, 0, 4.36, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.40, 0, 4.36, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.45, 0, 4.36, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.50, 0, 4.48, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.55, 0, 4.41, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.60, 0, 4.36, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.65, 0, 4.36, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.70, 0, 4.53, 0.02, 150, true),
		new DielectricMaterial(NYP, NP155F, Core, 0.71, 0, 4.43, 0.02, 150, true), // >0.70mm
		new DielectricMaterial(SYTECH, S1000, Core, 0.075, 0, 4.14, 0.018, 180, true, 10),
		new DielectricMaterial(SYTECH, S1000, Core, 0.10,  0, 4.11, 0.018, 180, true, 10),
		new DielectricMaterial(SYTECH, S1000, Core, 0.13,  0, 4.03, 0.018, 180, true, 10),
		new DielectricMaterial(SYTECH, S1000, Core, 0.15,  0, 4.53, 0.018, 180, true, 10),
		new DielectricMaterial(SYTECH, S1000, Core, 0.20,  0, 4.42, 0.018, 180, true, 10),
		new DielectricMaterial(SYTECH, S1000, Core, 0.25,  0, 4.29, 0.018, 180, true, 10),
		new DielectricMaterial(SYTECH, S1000, Core, 0.30,  0, 4.56, 0.018, 180, true, 10),
	};
}

class DielectricMaterial
{
	public string Manufacturer { get; set; }
	public string Model { get; set; }
	public string Construction { get; set; }
	public double NominalThickness { get; set; }
	public double ResinContent { get; set; }
	public double DielectricConstant { get; set; }
	public double LossTangent { get; set; }
	public int GlassTransitionTemperature { get; set; }
	public long ExpectedLayerCount { get; set; }
	public bool IsCore { get; set; }
	
	public DielectricMaterial(string manufacturer, string model, string construction, double nominalThickness, double resinContent, double dK, double dF, int Tg, bool isCore, long expectedLayerCount = 0)
	{
		Manufacturer = manufacturer;
		Model = model;
		Construction = construction;
		NominalThickness = nominalThickness;
		ResinContent = resinContent;
		GlassTransitionTemperature = Tg;
		DielectricConstant = dK;
		LossTangent = dF;
		IsCore = isCore;
		ExpectedLayerCount = expectedLayerCount;
	}
}

[JsonPolymorphic]
[JsonDerivedType(typeof(CopperLayerData))]
[JsonDerivedType(typeof(DielectricLayerData))]
abstract class BaseLayerData { }

class CopperLayerData : BaseLayerData
{
	public string Type => "Copper";
	public double Weight { get; set; }
	public double Thickness { get; set; }
}

class DielectricLayerData : BaseLayerData
{
	public string Type => "Dielectric";
	public DielectricMaterial Material { get; set; }
	public double Thickness { get; set; }
}

class JLCStackupRequest
{
	[JsonPropertyName("cuprumThickness")]
	public double OuterCopperThickness { get; set; }
	[JsonPropertyName("insideCuprumThickness")]
	public double InnerCopperThickness { get; set; }
	[JsonPropertyName("stencilLayer")]
	public long LayerCount { get; set; }
	[JsonPropertyName("stencilPly")]
	public double BoardThickness { get; set; }
}

class JLCStackupResponse
{
	[JsonPropertyName("code")]
	public long ResponseCode { get; set; }
	[JsonPropertyName("data")]
	public JLCStackupTemplate[]? Templates { get; set; }
	[JsonPropertyName("message")]
	public string? Message { get; set; }
}

class JLCStackupTemplate
{
	[JsonPropertyName("templateName")]
	public string? TemplateName { get; set; }
	[JsonPropertyName("showName")]
	public string? DisplayName { get; set; }
	[JsonPropertyName("stencilLayer")]
	public long LayerCount { get; set; }
	[JsonPropertyName("stencilPly")]
	public double BoardThickness { get; set; }
	[JsonPropertyName("cuprumThickness")]
	public double OuterCopperWeight { get; set; }
	[JsonPropertyName("insideCuprumThickness")]
	public double InnerCopperWeight { get; set; }
	[JsonPropertyName("defaultFlag")]
	public bool Default { get; set; }
	[JsonPropertyName("expeditedFlag")]
	public bool Expedited { get; set; }
	[JsonPropertyName("enableFlag")]
	public bool Enabled { get; set; }
	[JsonPropertyName("sort")]
	public long Sort { get; set; }
	[JsonPropertyName("fixedFee")]
	public double FixedFee { get; set; }
	[JsonPropertyName("coefficient")]
	public double Coefficient { get; set; }
	[JsonPropertyName("iaminationList")]
	public JLCLamination[]? Laminations { get; set; }
	[JsonPropertyName("impedanceTemplateCode")]
	public string? ImpedanceTemplateCode { get; set; }
	[JsonPropertyName("laminationType")]
	public long LaminationType { get; set; }
	[JsonPropertyName("compressionThickness")]
	public double CompressionThickness { get; set; }
}


enum JLCLaminationType
{
	Line = 1,
	Prepreg = 2,
	Core = 3
}

class JLCLamination
{
	[JsonPropertyName("content")]
	public string? Content { get; set; }
	[JsonPropertyName("contentKey")]
	public string? ContentKey { get; set; }
	[JsonPropertyName("deleteFlag")]
	public bool DeleteFlag { get; set; }
	[JsonPropertyName("iaminationType")]
	public JLCLaminationType LaminationType { get; set; }
	[JsonPropertyName("impedanceIaminationAccessId")]
	public string? ImpedanceLaminationAccessID { get; set; }
	[JsonPropertyName("impedanceIaminationKeyId")]
	public long ImpedanceLaminationKeyID { get; set; }
	[JsonPropertyName("sort")]
	public long Sort { get; set; }

	private T? GetContent<T>() where T : class
	{
		return this.Content == null ? null : JsonSerializer.Deserialize<T>(this.Content);
	}

	public JLCLaminationLineContent? GetLineContent()
	{
		return GetContent<JLCLaminationLineContent>();
	}

	public JLCLaminationPrepregContent? GetPrepregContent()
	{
		return GetContent<JLCLaminationPrepregContent>();
	}

	public JLCLaminationCoreContent? GetCoreContent()
	{
		return GetContent<JLCLaminationCoreContent>();
	}
}

class JLCLaminationLineContent
{
	[JsonPropertyName("LineLayer")]
	public string? LineLayer { get; set; }
	[JsonPropertyName("lineMaterialType")]
	public string? MaterialType { get; set; }
	[JsonPropertyName("LineThickness")]
	public string? Thickness { get; set; }
	[JsonPropertyName("coreBoardRemark")]
	public string? CoreBoardRemark { get; set; }
}

class JLCLaminationPrepregContent
{
	[JsonPropertyName("preLayer")]
	public string? PrepregLayer { get; set; }
	[JsonPropertyName("preMaterialType")]
	public string? MaterialType { get; set; }
	[JsonPropertyName("preThickness")]
	public string? Thickness { get; set; }
	[JsonPropertyName("coreBoardRemark")]
	public string? CoreBoardRemark { get; set; }
}

class JLCLaminationCoreContent
{
	[JsonPropertyName("coreBoardLayer1")]
	public string? CoreLayer1 { get; set; }
	[JsonPropertyName("coreBoardMaterialType1")]
	public string? MaterialType1 { get; set; }
	[JsonPropertyName("coreBoardThickness1")]
	public string? Thickness1 { get; set; }
	[JsonPropertyName("coreBoardLayer2")]
	public string? CoreLayer2 { get; set; }
	[JsonPropertyName("coreBoardMaterialType2")]
	public string? MaterialType2 { get; set; }
	[JsonPropertyName("coreBoardThickness2")]
	public string? Thickness2 { get; set; }
	[JsonPropertyName("coreBoardLayer3")]
	public string? CoreLayer3 { get; set; }
	[JsonPropertyName("coreBoardMaterialType3")]
	public string? MaterialType3 { get; set; }
	[JsonPropertyName("coreBoardThickness3")]
	public string? Thickness3 { get; set; }
	[JsonPropertyName("coreBoardRemark")]
	public string? CoreBoardRemark { get; set; }

	[JsonIgnore]
	public JLCLaminationCoreContentEntry[] Entries => new [] {
		new JLCLaminationCoreContentEntry { CoreLayer = CoreLayer1, MaterialType = MaterialType1, Thickness = Thickness1 },
		new JLCLaminationCoreContentEntry { CoreLayer = CoreLayer2, MaterialType = MaterialType2, Thickness = Thickness2 },
		new JLCLaminationCoreContentEntry { CoreLayer = CoreLayer3, MaterialType = MaterialType3, Thickness = Thickness3 },
	};
}

class JLCLaminationCoreContentEntry
{
	public string? CoreLayer { get; set; }
	public string? MaterialType { get; set; }
	public string? Thickness { get; set; }
}