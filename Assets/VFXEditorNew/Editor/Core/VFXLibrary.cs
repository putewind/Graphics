using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = System.Object;

namespace UnityEditor.VFX
{
    // Attribute used to register VFX type to library
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class VFXInfoAttribute : Attribute
    {
        public VFXInfoAttribute(bool register = true)
        {
            this.register = register;
        }

        public bool register = true;
        public string category = "";
        public Type type = null; // Used by slots to map types to slot

        public static VFXInfoAttribute Get(Object obj)
        {
            return Get(obj.GetType());
        }

        public static VFXInfoAttribute Get(Type type)
        {
            var attribs = type.GetCustomAttributes(typeof(VFXInfoAttribute), false);
            return attribs.Length == 1 ? (VFXInfoAttribute)attribs[0] : null;
        }
    }

    class VFXModelDescriptor<T> where T : VFXModel
    {
        public VFXModelDescriptor(T template)
        {
            m_Template = template;
        }

        public string name { get { return m_Template.name; } }
        public VFXInfoAttribute info { get { return VFXInfoAttribute.Get(m_Template); } }
        public Type modelType { get { return m_Template.GetType(); } }

        public bool AcceptParent(VFXModel parent, int index = -1)
        {
            return parent.AcceptChild(m_Template, index);
        }

        virtual public T CreateInstance()
        {
            return (T)ScriptableObject.CreateInstance(m_Template.GetType());
        }

        protected T m_Template;
    }

    class VFXModelDescriptorParameters : VFXModelDescriptor<VFXParameter>
    {
        public VFXModelDescriptorParameters(VFXParameter template):base(template)
        {
        }

        public override VFXParameter CreateInstance()
        {
            var instance = base.CreateInstance();
            instance.type = m_Template.type;
            return instance;
        }
    }

    static class VFXLibrary
    {
        public static IEnumerable<VFXModelDescriptor<VFXContext>> GetContexts()     { LoadIfNeeded(); return m_ContextDescs; }
        public static IEnumerable<VFXModelDescriptor<VFXBlock>> GetBlocks()         { LoadIfNeeded(); return m_BlockDescs; }
        public static IEnumerable<VFXModelDescriptor<VFXOperator>> GetOperators()   { LoadIfNeeded(); return m_OperatorDescs; }
        public static IEnumerable<VFXModelDescriptor<VFXSlot>> GetSlots()           { LoadSlotsIfNeeded(); return m_SlotDescs.Values; }
        public static IEnumerable<VFXModelDescriptorParameters> GetParameters()     { LoadIfNeeded(); return m_ParametersDescs; }

        public static VFXModelDescriptor<VFXSlot> GetSlot(System.Type type)
        { 
            LoadSlotsIfNeeded(); 
            VFXModelDescriptor<VFXSlot> desc;
            m_SlotDescs.TryGetValue(type,out desc);
            return desc;
        }

        public static void LoadIfNeeded()
        {
            if (m_Loaded)
                return;

            lock (m_Lock)
            {
                if (!m_Loaded)
                    Load();
            }
        }
        
        public static void Load()
        {
            lock(m_Lock)
            {
                LoadSlotsIfNeeded();
                m_ContextDescs = LoadModels<VFXContext>();
                m_BlockDescs = LoadModels<VFXBlock>();
                m_OperatorDescs = LoadModels<VFXOperator>();

                m_ParametersDescs = m_SlotDescs.Select(s =>
                {
                    var param = ScriptableObject.CreateInstance<VFXParameter>();
                    param.type = s.Key;
                    var desc = new VFXModelDescriptorParameters(param);
                    return desc;
                }).ToList();

                m_Loaded = true;

                // Debug
                Debug.Log("ALL REGISTERED SLOTS:");
                foreach (var slot in m_SlotDescs)
                {
                    Debug.Log(slot.Key + " -> " + slot.Value.modelType);
                }
            }
        }

        private static void LoadSlotsIfNeeded()
        {
            if (m_SlotLoaded)
                return;

            lock (m_Lock)
            {
                if (!m_SlotLoaded)
                {
                    m_SlotDescs = LoadSlots();
                    m_SlotLoaded = true;
                }
            }
        }

        private static List<VFXModelDescriptor<T>> LoadModels<T>() where T : VFXModel
        {
            var modelTypes = FindConcreteSubclasses<T>();
            var modelDescs = new List<VFXModelDescriptor<T>>();
            foreach (var modelType in modelTypes)
            {
                try
                {
                    T instance = (T)ScriptableObject.CreateInstance(modelType);
                    modelDescs.Add(new VFXModelDescriptor<T>(instance));
                }
                catch (Exception e)
                {
                    Debug.LogError("Error while loading model from type " + modelType + ": " + e);
                }
            }

            return modelDescs.OrderBy(o => o.name).ToList();
        }

        private static Dictionary<Type, VFXModelDescriptor<VFXSlot>> LoadSlots()
        {
            var slotTypes = FindConcreteSubclasses<VFXSlot>();
            var dictionary = new Dictionary<Type, VFXModelDescriptor<VFXSlot>>();
            foreach (var slotType in slotTypes)
            {
                try
                {
                    Type boundType = VFXInfoAttribute.Get(slotType).type; // Not null as it was filtered before
                    if (boundType != null)
                    {
                        if (dictionary.ContainsKey(boundType))
                            throw new Exception(boundType + " was already bound to a slot type");

                        VFXSlot instance = (VFXSlot)ScriptableObject.CreateInstance(slotType);
                        dictionary[boundType] = new VFXModelDescriptor<VFXSlot>(instance);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("Error while loading slot from type " + slotType + ": " + e);
                }
            }
            return dictionary;
        }

        private static IEnumerable<Type> FindConcreteSubclasses<T>()
        {
            List<Type> types = new List<Type>();
            foreach (var domainAssembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] assemblyTypes = null;
                try
                {
                    assemblyTypes = domainAssembly.GetTypes();
                }
                catch (Exception)
                {
                    Debug.Log("Cannot access assembly: " + domainAssembly);
                    assemblyTypes = null;
                }
                if (assemblyTypes != null)
                    foreach (var assemblyType in assemblyTypes)
                        if (assemblyType.IsSubclassOf(typeof(T)) && !assemblyType.IsAbstract)
                            types.Add(assemblyType);
            }
            return types.Where(type => type.GetCustomAttributes(typeof(VFXInfoAttribute), false).Length == 1);
        }

        private static volatile List<VFXModelDescriptor<VFXContext>> m_ContextDescs;
        private static volatile List<VFXModelDescriptor<VFXOperator>> m_OperatorDescs;
        private static volatile List<VFXModelDescriptor<VFXBlock>> m_BlockDescs;
        private static volatile List<VFXModelDescriptorParameters> m_ParametersDescs;
        private static volatile Dictionary<Type,VFXModelDescriptor<VFXSlot>> m_SlotDescs;

        private static Object m_Lock = new Object();
        private static volatile bool m_Loaded = false;
        private static volatile bool m_SlotLoaded = false;
    }
}
