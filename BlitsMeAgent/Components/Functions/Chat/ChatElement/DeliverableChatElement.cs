﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Gwupe.Agent.Components.Functions.Chat.ChatElement
{
    public enum ChatDeliveryState
    {
        NotAttempted = 1,
        Trying,
        Delivered,
        FailedTrying,
        Failed
    };

    public abstract class DeliverableChatElement : BaseChatElement
    {
        private DateTime _deliveryTime;
        public DateTime DeliveryTime
        {
            get { return _deliveryTime; }
            private set
            {
                _deliveryTime = value;
                OnPropertyChanged("DeliveryTime");
            }
        }

        private ChatDeliveryState _deliveryState = ChatDeliveryState.NotAttempted;
        public ChatDeliveryState DeliveryState
        {
            get
            {
                return _deliveryState;
            }
            set
            {
                _deliveryState = value;
                OnPropertyChanged("DeliveryState");
            }
        }
    }
}
