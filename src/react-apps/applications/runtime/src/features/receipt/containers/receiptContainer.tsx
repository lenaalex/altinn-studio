import * as moment from 'moment';
import { useState } from 'react';
import * as React from 'react';
import { useSelector } from 'react-redux';
import { RouteChildrenProps, withRouter } from 'react-router';
import AltinnContentIconReceipt from '../../../../../shared/src/components/atoms/AltinnContentIconReceipt';
import AltinnContentLoader from '../../../../../shared/src/components/molecules/AltinnContentLoader';
import ReceiptComponent from '../../../../../shared/src/components/organisms/AltinnReceipt';
import { getLanguageFromKey, getUserLanguage } from '../../../../../shared/src/utils/language';
import { IRuntimeState } from '../../../types';
import { IInstance } from './../../../../../shared/src/types/index.d';
import returnInstanceAttachments from './../../../../../shared/src/utils/returnInstanceAttachments';
import { returnUrlToMessagebox } from './../../../../../shared/src/utils/urlHelper';
import InstanceDataActions from './../../../shared/resources/instanceData/instanceDataActions';
import OrgsActions from './../../../shared/resources/orgs/orgsActions';

export interface IReceiptContainerProps extends RouteChildrenProps {
}

export const returnInstanceMetaDataObject = (
  orgsData: any,
  languageData: any,
  profileData: any,
  instanceGuid: string,
  userLanguageString: string,
  lastChangedDateTime: string,
  org: any,
  ): {} => {
  const obj: any = {};

  obj[getLanguageFromKey('receipt.date_sent', languageData)] = lastChangedDateTime;

  let sender: string = '';
  if (profileData.profile && profileData.profile.party.person.ssn) {
    sender = `${profileData.profile.party.person.ssn}-${profileData.profile.party.name}`;
  } else if (profileData) {
    sender = `${profileData.profile.party.orgNumber}-${profileData.profile.party.name}`;
  }
  obj[getLanguageFromKey('receipt.sender', languageData)] = sender;

  if (orgsData[org]) {
    obj[getLanguageFromKey('receipt.receiver', languageData)] = orgsData[org].name[userLanguageString];
  } else {
    // This is only related to testing in Altinn Studio Dev
    obj[getLanguageFromKey('receipt.receiver', languageData)] = 'Error: Receiver org not found';
  }

  obj[getLanguageFromKey('receipt.ref_num', languageData)] = instanceGuid;

  return obj;
};

const ReceiptContainer = (props: IReceiptContainerProps ) => {
  const [appName, setAppName] = React.useState('');
  const [attachments, setAttachments] = useState([]);
  const [lastChangedDateTime, setLastChangedDateTime] = useState('');
  const [instanceMetaObject, setInstanceMetaObject] = useState({});
  const [userLanguage, setUserLanguage] = React.useState('nb');

  const allOrgs: any = useSelector((state: IRuntimeState) => state.organisationMetaData.allOrgs);
  const applicationMetadata: any = useSelector((state: IRuntimeState) => state.applicationMetadata.applicationMetadata);
  const instance: IInstance = useSelector((state: IRuntimeState) => state.instanceData.instance);
  const language: any = useSelector((state: IRuntimeState) => state.language.language);
  const profile: any = useSelector((state: IRuntimeState) => state.profile);

  const origin = window.location.origin;
  const routeParams: any = props.match.params;

  const isLoading = (): boolean => {
    return (
      !attachments ||
      !instanceMetaObject ||
      !lastChangedDateTime ||
      !appName ||
      !allOrgs ||
      !profile ||
      !instance ||
      !lastChangedDateTime
    );
  };

  React.useEffect(() => {
    setUserLanguage(getUserLanguage());
    OrgsActions.fetchOrgs();
    InstanceDataActions.getInstanceData(routeParams.partyId, routeParams.instanceGuid);
  }, []);

  React.useEffect(() => {
    if (allOrgs != null && profile.profile && instance && instance.org && allOrgs) {
      const obj = returnInstanceMetaDataObject(
        allOrgs, language, profile, routeParams.instanceGuid, userLanguage, lastChangedDateTime, instance.org,
      );
      setInstanceMetaObject(obj);
    }
  }, [allOrgs, profile, instance, lastChangedDateTime]);

  React.useEffect(() => {
    if (applicationMetadata && applicationMetadata.title) {
      setAppName(applicationMetadata.title[userLanguage]);
    }
  }, [applicationMetadata, userLanguage]);

  React.useEffect(() => {
    if (instance && instance.data) {
      const attachmentsResult = returnInstanceAttachments(instance.data);
      setAttachments(attachmentsResult);

      const defaultDataElementLastChangedDateTime = instance.data
        .filter((elem) => elem.elementType === 'default')[0]
        .lastChangedDateTime;

      setLastChangedDateTime(moment(defaultDataElementLastChangedDateTime).format('DD.MM.YYYY / HH:mm'));
    }
  }, [instance]);

  // TODO: Implement PDF support when implemented
  // const pdf = [{
  //   name: 'InnsendtSkjema.pdf',
  //   iconClass: 'reg reg-attachment',
  //   url: 'http://url.til.skjema/fil.pdf',
  // }];

  return (
    <>
      {isLoading() &&
        <AltinnContentLoader  width={705} height={561}>
          <AltinnContentIconReceipt/>
        </AltinnContentLoader>
      }
      {!isLoading() &&
        <ReceiptComponent
          attachments={attachments}
          body={getLanguageFromKey('receipt.body', language)}
          collapsibleTitle={getLanguageFromKey('receipt.attachments', language)}
          instanceMetaDataObject={instanceMetaObject}
          subtitle={getLanguageFromKey('receipt.subtitle', language)}
          subtitleurl={returnUrlToMessagebox(origin)}
          title={`${appName} ${getLanguageFromKey('receipt.title_part_is_submitted', language)}`}
          titleSubmitted={getLanguageFromKey('receipt.title_submitted', language)}
        />
      }
    </>
  );

};

export default withRouter(ReceiptContainer);