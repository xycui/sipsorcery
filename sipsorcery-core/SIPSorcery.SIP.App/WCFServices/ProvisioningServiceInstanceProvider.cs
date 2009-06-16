﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Configuration;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.Text;
using SIPSorcery.CRM;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App {

    /*
    <service name="Sample.HelloWCF" behaviorConfiguration="MyInstanceProviderBehavior">

    ...

    <behaviors>
        <serviceBehaviors>
            <behavior name="MyInstanceProviderBehavior">
                <MyInstanceProvider/>
            </behavior>
        </serviceBehaviors>
    </behaviors>

    ...

    <extensions>
        <behaviorExtensions>
            <add name="MyInstanceProvider"
                 type="CustomBehaviorSample.InstanceProviderExtensionElement, Behavior, 

    Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"/>
        </behaviorExtensions>
    </extensions>
    */

    public class InstanceProviderExtensionElement : BehaviorExtensionElement {

        private static ILog logger = AppState.GetLogger("provisioningsvc");

        private static readonly string m_storageTypeKey = Persistence.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = Persistence.PERSISTENCE_STORAGECONNSTR_KEY;

        private static StorageTypes m_serverStorageType;
        private static string m_serverStorageConnStr;

        protected override object CreateBehavior() {

            try {
                m_serverStorageType = (ConfigurationManager.AppSettings[m_storageTypeKey] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[m_storageTypeKey]) : StorageTypes.Unknown;
                m_serverStorageConnStr = ConfigurationManager.AppSettings[m_connStrKey];

                if (m_serverStorageType == StorageTypes.Unknown || m_serverStorageConnStr.IsNullOrBlank()) {
                    throw new ApplicationException("The Provisioning Web Service cannot start with no persistence settings specified.");
                }

                return new ProvisioningServiceInstanceProvider(
                    SIPAssetPersistorFactory.CreateSIPAccountPersistor(m_serverStorageType, m_serverStorageConnStr),
                    SIPAssetPersistorFactory.CreateDialPlanPersistor(m_serverStorageType, m_serverStorageConnStr),
                    SIPAssetPersistorFactory.CreateSIPProviderPersistor(m_serverStorageType, m_serverStorageConnStr),
                    SIPAssetPersistorFactory.CreateSIPProviderBindingPersistor(m_serverStorageType, m_serverStorageConnStr),
                    SIPAssetPersistorFactory.CreateSIPRegistrarBindingPersistor(m_serverStorageType, m_serverStorageConnStr),
                    SIPAssetPersistorFactory.CreateSIPDialoguePersistor(m_serverStorageType, m_serverStorageConnStr),
                    SIPAssetPersistorFactory.CreateSIPCDRPersistor(m_serverStorageType, m_serverStorageConnStr),
                    new CustomerSessionManager(m_serverStorageType, m_serverStorageConnStr),
                    new SIPDomainManager(m_serverStorageType, m_serverStorageConnStr),
                     (e) => { logger.Debug(e.Message); } );
            }
            catch (Exception excp) {
                logger.Error("Exception InstanceProviderExtensionElement CreateBehavior. " + excp.Message);
                throw;
            }
        }

        public override Type BehaviorType {
            get { return typeof(ProvisioningServiceInstanceProvider); }
        }
    }

    public class ProvisioningServiceInstanceProvider : IInstanceProvider, IServiceBehavior {

        private SIPAssetPersistor<SIPAccount> m_sipAccountPersistor;
        private SIPAssetPersistor<SIPDialPlan> m_sipDialPlanPersistor;
        private SIPAssetPersistor<SIPProvider> m_sipProviderPersistor;
        private SIPAssetPersistor<SIPProviderBinding> m_sipProviderBindingsPersistor;
        private SIPAssetPersistor<SIPRegistrarBinding> m_sipRegistrarBindingsPersistor;
        private SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;
        private SIPAssetPersistor<SIPCDRAsset> m_sipCDRPersistor;
        private SIPAssetPersistor<Customer> m_crmCustomerPersistor;
        private CustomerSessionManager m_crmSessionManager;
        private SIPDomainManager m_sipDomainManager;
        private SIPMonitorLogDelegate m_logDelegate;

        public ProvisioningServiceInstanceProvider() {
        }

        public ProvisioningServiceInstanceProvider(
            SIPAssetPersistor<SIPAccount> sipAccountPersistor,
            SIPAssetPersistor<SIPDialPlan> sipDialPlanPersistor,
            SIPAssetPersistor<SIPProvider> sipProviderPersistor,
            SIPAssetPersistor<SIPProviderBinding> sipProviderBindingsPersistor,
            SIPAssetPersistor<SIPRegistrarBinding> sipRegistrarBindingsPersistor,
            SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor,
            SIPAssetPersistor<SIPCDRAsset> sipCDRPersistor,
            CustomerSessionManager crmSessionManager,
            SIPDomainManager sipDomainManager,
            SIPMonitorLogDelegate log) {

            m_sipAccountPersistor = sipAccountPersistor;
            m_sipDialPlanPersistor = sipDialPlanPersistor;
            m_sipProviderPersistor = sipProviderPersistor;
            m_sipProviderBindingsPersistor = sipProviderBindingsPersistor;
            m_sipRegistrarBindingsPersistor = sipRegistrarBindingsPersistor;
            m_sipDialoguePersistor = sipDialoguePersistor;
            m_sipCDRPersistor = sipCDRPersistor;
            m_crmCustomerPersistor = crmSessionManager.CustomerPersistor;
            m_crmSessionManager = crmSessionManager;
            m_sipDomainManager = sipDomainManager;
            m_logDelegate = log;
        }

        public object GetInstance(InstanceContext instanceContext) {
            return GetInstance(instanceContext, null);
        }

        public object GetInstance(InstanceContext instanceContext, Message message) {
            return new SIPProvisioningWebService(
                m_sipAccountPersistor,
                m_sipDialPlanPersistor,
                m_sipProviderPersistor,
                m_sipProviderBindingsPersistor,
                m_sipRegistrarBindingsPersistor,
                m_sipDialoguePersistor,
                m_sipCDRPersistor,
                m_crmSessionManager,
                m_sipDomainManager,
                m_logDelegate);
        }

        public void ReleaseInstance(InstanceContext instanceContext, object instance) { }

        public void ApplyDispatchBehavior(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase) {
            foreach (ChannelDispatcherBase channelDispatcherBase in serviceHostBase.ChannelDispatchers) {
                ChannelDispatcher channelDispatcher = channelDispatcherBase as ChannelDispatcher;
                if (channelDispatcher != null) {
                    foreach (EndpointDispatcher endpoint in channelDispatcher.Endpoints) {
                        endpoint.DispatchRuntime.InstanceProvider = this;
                    }
                }
            }
        }

        public void AddBindingParameters(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase, System.Collections.ObjectModel.Collection<ServiceEndpoint> endpoints, BindingParameterCollection bindingParameters) {
        }

        public void Validate(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase) {
        }
    }
}