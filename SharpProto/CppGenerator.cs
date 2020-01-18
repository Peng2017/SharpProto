﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SharpProto
{
  public class CppGenerator : GeneratorBase, IGenerator
  {
    protected readonly Dictionary<string, string> primitiveTypeMapping = new Dictionary<string, string>()
        {
            {"String", "std::string" },
            {"Int32", "int" },
            {"Int64", "long long" },
            {"Single", "float" },
            {"Double", "double" },
        };

    protected readonly IList<string> nonReferenceTypes = new List<string>()
        {
            "Int32",
            "Int64",
            "Single",
            "Double"
        };

    public CppGenerator()
    {
    }

    public bool Generate(string Filename, string OutputDir)
    {
      var assembly = base.CompileProto(Filename);

      var types = base.ValidateProtos(assembly);

      var stem = Path.GetFileNameWithoutExtension(Filename);

      var hFilename = Path.Join(OutputDir, stem + ".pb.h");

      File.WriteAllText(hFilename, GenerateHeader(stem, types));
      return true;
    }

    private string GenerateHeader(string stem, Type[] types)
    {
      var guard = "_" + stem.ToUpper() + "_";

      var template =
@"// DO NOT MODIFY! CODE GENERATED BY SHARPPROTO.
#ifndef {0}
#define {0}

#include <string>
#include <sstream>

{1}

#endif // {0}";

      var sb = new StringBuilder();
      sb.AppendFormat("namespace {0} {{\n", types[0].Namespace);

      foreach (Type t in types)
      {
        sb.Append(GenerateClassDef(t));
        sb.AppendLine();
        sb.AppendLine();
      }

      sb.AppendFormat("}}  // namespace {0}\n", types[0].Namespace);

      return string.Format(template, guard, sb.ToString());
    }

    private string GetTypeName(string name)
    {
      return primitiveTypeMapping.ContainsKey(name) ? primitiveTypeMapping[name] : name;
    }

    private string GenerateClassDef(Type t)
    {
      var sb = new StringBuilder();

      sb.AppendFormat("class {0} {{\n", t.Name);

      var sbPublic = new StringBuilder();
      var sbPrivate = new StringBuilder();
      var sbDebugString = new StringBuilder();
      var sbSerializeAsString = new StringBuilder();
      var sbParseFromString = new StringBuilder();

      sbDebugString.AppendFormat("  std::string DebugString() const {{\n");
      sbDebugString.AppendFormat("    std::ostringstream ss;\n");

      sbSerializeAsString.AppendFormat("  std::string SerializeAsString() const {{\n");
      sbSerializeAsString.AppendFormat("    std::ostringstream ss;\n");

      sbParseFromString.AppendFormat("  bool ParseFromString(const std::string& str) {{\n");
      sbParseFromString.AppendFormat("    std::istringstream ss(str);\n");
      sbParseFromString.AppendFormat("    int head;\n");            
      sbParseFromString.AppendFormat("    while (!ss.eof()) {{\n");
      sbParseFromString.AppendFormat("      ss.read(reinterpret_cast<char*>(&head), 4);\n");
      sbParseFromString.AppendFormat("      int tag = head >> 3;\n");
      sbParseFromString.AppendFormat("      int type = head & 0x07;\n");
      sbParseFromString.AppendFormat("      if (false) {{ \n");
      sbParseFromString.AppendFormat("      // DO NOTHING HERE \n");
      sbParseFromString.AppendFormat("      }}");


      foreach (var fieldInfo in t.GetFields())
      {
        var fieldAttr = fieldInfo.GetCustomAttribute<FieldAttribute>();
        var fieldType = fieldInfo.FieldType;
        var isMessage = fieldType.GetCustomAttribute<MessageAttribute>() != null;

        if (fieldType.IsGenericType)
        {
          if (fieldType.GetGenericTypeDefinition() == typeof(IList<>))
          {
            var argType = fieldType.GetGenericArguments()[0];
            throw new NotSupportedException("repeated fields not supported yet");
          }
        }
        else
        {
          // primitive types, e.g int
          var reference = nonReferenceTypes.Contains(fieldType.Name) ? "" : "&";
          var typeName = GetTypeName(fieldType.Name);
          sbPublic.AppendFormat("  const {0}{2} {1}() const {{ return {1}_; }}\n", typeName, fieldInfo.Name, reference);
          sbPublic.AppendFormat("  void set_{1}(const {0}{2} val) {{ {1}_ = val; }}\n", typeName, fieldInfo.Name, reference);
          sbPublic.AppendFormat("  {0}* mutable_{1}() {{ return &{1}_; }}\n", typeName, fieldInfo.Name);

          sbPrivate.AppendFormat("  " + WriteFiledComment(fieldInfo));
          sbPrivate.AppendFormat("  {0} {1}_;\n", typeName, fieldInfo.Name);

          DebugStringField(sbDebugString, fieldInfo, 4);

          SerializeField(sbSerializeAsString, fieldInfo, 4);

          DeseializeField(sbParseFromString, fieldInfo, 8);
        }
      }

      sbDebugString.Append("    return ss.str();\n");
      sbDebugString.Append("  }\n");

      sbSerializeAsString.Append("    return ss.str();\n");
      sbSerializeAsString.Append("  }\n");

      sbParseFromString.AppendFormat("\n");
      sbParseFromString.AppendFormat("    }}\n");
      sbParseFromString.Append("    return true;\n");
      sbParseFromString.Append("  }\n");

      sb.AppendFormat(" public:\n");

      sb.Append(sbPublic);
      sb.AppendLine();
      sb.Append(sbDebugString);
      sb.AppendLine();
      sb.Append(sbSerializeAsString);
      sb.AppendLine();
      sb.Append(sbParseFromString);
      sb.AppendLine();

      sb.AppendFormat(" private:\n");

      sb.Append(sbPrivate);

      sb.AppendFormat("}};\n");

      return sb.ToString();
    }

    private FieldEncodingType GetFieldEncodingType(Type fieldtype)
    {
      if (fieldtype == typeof(Int32) || fieldtype == typeof(Single)) return FieldEncodingType.FieldEncodingType32Bits;
      if (fieldtype == typeof(Int64) || fieldtype == typeof(Double)) return FieldEncodingType.FieldEncodingType64Bits;
      return FieldEncodingType.FieldEncodingTypeFixedLength;
    }

    private string WriteLVal(string name, int bytes)
    {
      return string.Format("ss.write(reinterpret_cast<const char*>(&{0}), {1});\n", name, bytes);
    }

    private string WriteRVal(string prefix, string name, int bits)
    {
      var varName = prefix + "_int" + bits;
      return string.Format((bits == 32 ? "int" : "long") + " {0} = {1}; " + WriteLVal(varName, bits / 8), varName, name);
    }

    private string WriteLiteral32(string name, int val)
    {
      var varName = name + "_int32";
      return string.Format("int {0} = {1}; ", varName, val) + WriteLVal(varName, 4);
    }

    private string WriteLiteral64(string name, long val)
    {
      var varName = name + "_int64";
      return string.Format("long {0} = {1}; ", varName, val) + WriteLVal(varName, 8);
    }

    private string ReadVal(string name, int bytes)
    {
      return string.Format("ss.read(reinterpret_cast<char*>(&{0}), {1});\n", name, bytes);
    }

    private string ReadString(string name, string length)
    {
      return string.Format("ss.read(&{0}[0], {1});\n", name, length);
    }

    private string WriteFiledComment(FieldInfo fieldInfo)
    {
      var fieldAttr = fieldInfo.GetCustomAttribute<FieldAttribute>();
      var fieldType = fieldInfo.FieldType;            
      return string.Format("// tag = {0}, name = {1}, type = {2}\n", fieldAttr.ID, fieldInfo.Name, fieldType.Name);
    }

    private void SerializeField(StringBuilder sb, FieldInfo fieldInfo, int tabSize)
    {     
      var fieldAttr = fieldInfo.GetCustomAttribute<FieldAttribute>();
      var fieldType = fieldInfo.FieldType;
      var isMessage = fieldType.GetCustomAttribute<MessageAttribute>() != null;

      var ind = "".PadLeft(tabSize, ' ');
      sb.Append(ind + "{\n");

      var indentation = ind + "  ";

      FieldEncodingType encodingType = GetFieldEncodingType(fieldType);

      int head = (fieldAttr.ID << 3) | (int)encodingType;
      sb.Append(indentation + WriteFiledComment(fieldInfo));
      sb.AppendFormat(indentation + WriteLiteral32(fieldInfo.Name + "_head", head));

      if (encodingType == FieldEncodingType.FieldEncodingType32Bits || encodingType == FieldEncodingType.FieldEncodingType64Bits)
      {
        sb.AppendFormat(indentation + WriteLVal(fieldInfo.Name + "_", encodingType == FieldEncodingType.FieldEncodingType32Bits ? 4 : 8));
      }
      else if (fieldType == typeof(string))
      {
        sb.AppendFormat(indentation + WriteRVal(fieldInfo.Name, fieldInfo.Name + "_.length()", 32));
        sb.AppendFormat(indentation + "ss << {0}_;\n", fieldInfo.Name);
      }
      else if (isMessage)
      {
        string strName = string.Format("tmp_{0}", fieldInfo.Name);
        sb.AppendFormat(indentation + "std::string {0} = {1}_.SerializeAsString();\n", strName, fieldInfo.Name);
        sb.AppendFormat(indentation + WriteRVal(strName, strName + ".length()", 32));
        sb.AppendFormat(indentation + "ss << {0};\n", strName);
      }      

      sb.Append(ind + "}\n");
    }   

    private void DeseializeField(StringBuilder sb, FieldInfo fieldInfo, int tabSize)
    {
      var fieldAttr = fieldInfo.GetCustomAttribute<FieldAttribute>();
      var fieldType = fieldInfo.FieldType;
      var isMessage = fieldType.GetCustomAttribute<MessageAttribute>() != null;

      var ind = "".PadLeft(tabSize, ' ');
      sb.AppendFormat(" else if (tag == {0}) {{\n", fieldAttr.ID);
      sb.Append(ind + WriteFiledComment(fieldInfo));

      FieldEncodingType encodingType = GetFieldEncodingType(fieldType);

      if (encodingType == FieldEncodingType.FieldEncodingType32Bits || encodingType == FieldEncodingType.FieldEncodingType64Bits)
      {
        sb.AppendFormat(ind + ReadVal(fieldInfo.Name + "_", encodingType == FieldEncodingType.FieldEncodingType32Bits ? 4 : 8));
      }
      else if (fieldType == typeof(string))
      {
        sb.AppendFormat(ind + "int length = 0;\n");
        sb.AppendFormat(ind +  ReadVal("length", 4));
        sb.AppendFormat(ind + "{0}_.resize(length);\n", fieldInfo.Name);
        sb.AppendFormat(ind + ReadString(fieldInfo.Name + "_", "length"));
      }
      else if (isMessage)
      {
        sb.AppendFormat(ind + "int length = 0;\n");
        sb.AppendFormat(ind + ReadVal("length", 4));
        sb.AppendFormat(ind + "std::string buffer;\n");
        sb.AppendFormat(ind + "buffer.resize(length);\n");
        sb.AppendFormat(ind + ReadString("buffer", "length"));
        sb.AppendFormat(ind + "{0}_.ParseFromString(buffer);\n", fieldInfo.Name);
      }

      sb.AppendFormat("".PadLeft(tabSize - 2, ' ') + "}}");
    }

    private void DebugStringField(StringBuilder sb, FieldInfo fieldInfo, int tabSize)
    {
      var fieldAttr = fieldInfo.GetCustomAttribute<FieldAttribute>();
      var fieldType = fieldInfo.FieldType;
      var isMessage = fieldType.GetCustomAttribute<MessageAttribute>() != null;

      var ind = "".PadLeft(tabSize, ' ');

      sb.AppendFormat(ind + "ss << \"{0}\"", fieldInfo.Name);
      if (isMessage)
      {
        sb.AppendFormat(" << \" {{\\n\";\n");
        sb.AppendFormat(ind + "ss << {0}_.DebugString();\n", fieldInfo.Name);
        sb.AppendFormat(ind + "ss << \"}}\\n\";\n");
      }
      else if (fieldType == typeof(string))
      {
        sb.AppendFormat(" << \" : \\\"\" << {0}_ << \"\\\"\\n\";\n", fieldInfo.Name);
      }
      else
      {
        sb.AppendFormat(" << \" : \" << {0}_ << '\\n';\n", fieldInfo.Name);
      }
    }
  }
}
