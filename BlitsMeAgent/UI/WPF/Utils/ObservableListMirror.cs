﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Threading;
using log4net;

namespace BlitsMe.Agent.UI.WPF.Utils
{
    abstract class ObservableListMirror<TIncomingType,TOutgoingType>
    {
        private static readonly ILog Logger =
            LogManager.GetLogger(typeof (ObservableListMirror<TIncomingType, TOutgoingType>));

        public ObservableCollection<TOutgoingType> List { get; private set; }
        protected readonly Dictionary<String,TOutgoingType> ListLookup = new Dictionary<String,TOutgoingType>();
        private PropertyInfo _sourceLookupProperty;
        protected readonly Dispatcher Dispatcher;

        protected ObservableListMirror(Dispatcher dispatcher)
        {
            this.Dispatcher = dispatcher;
            // Create a local copy of the list
            List = new ObservableCollection<TOutgoingType>();
        }

        public void SetList(ObservableCollection<TIncomingType> list, String sourcePropertyName)
        {
            _sourceLookupProperty = typeof(TIncomingType).GetProperty(sourcePropertyName);
            if(_sourceLookupProperty == null)
            {
                Logger.Error("Failed to get property " + sourcePropertyName + " of " +
                                    typeof(TIncomingType).Name);
                throw new Exception("Failed to get property " + sourcePropertyName + " of " +
                                    typeof (TIncomingType).Name);
            }
            List.Clear();
            foreach (var listElement in list)
            {
                AddElement(listElement);
            }
            list.CollectionChanged += ListChanged;
        }

        protected abstract TOutgoingType CreateNew(TIncomingType sourceObject);

        private void AddElement(TIncomingType listElement)
        {
            TOutgoingType newElement;
            var lookupKey =
                (String) (_sourceLookupProperty.GetValue(listElement, null));
            if (ListLookup.ContainsKey(lookupKey))
            {
                newElement = ListLookup[lookupKey];
            }
            else
            {
                try
                {
                    newElement = CreateNew(listElement);
                    ListLookup.Add((String)_sourceLookupProperty.GetValue(listElement, null), newElement);
                    List.Add(newElement);
                }
                catch (Exception e)
                {
                    Logger.Error("Cannot create new listElement : " + e.Message, e);
                }
            }
        }

        private void ListChanged(object sender, NotifyCollectionChangedEventArgs eventArgs)
        {
            // Modifications to the local collection must be done in UI thread.. and so... :)
            if (Dispatcher.CheckAccess())
            {
                ProcessAction(eventArgs);
            }
            else
            {
                this.Dispatcher.Invoke(new Action(() => ProcessAction(eventArgs)));
            }
        }

        private void ProcessAction(NotifyCollectionChangedEventArgs eventArgs)
        {
            switch (eventArgs.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    {
                        if (eventArgs.NewItems != null)
                        {
                            foreach (TIncomingType newItem in eventArgs.NewItems)
                            {
                                AddElement(newItem);
                            }
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    {
                        List.Clear();
                        ListLookup.Clear();
                    }
                    break;
                default:
                    {
                        Logger.Warn("Roster changed with unhandled action " +
                                    eventArgs.Action);
                    }
                    break;
            }
        }
    }
}
