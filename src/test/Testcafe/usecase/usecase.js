import { t, ClientFunction, Selector } from 'testcafe';
import DesignerPage from '../page-objects/designerPage';
import { AutoTestUser } from '../TestData';
import config from '../config.json';
import App from '../app';

let app = new App();
let designerPage = new DesignerPage();
let environment = (process.env.ENV).toLowerCase();
let appName = config[environment].deployApp;

const getLocation = ClientFunction(() => document.location.href);

fixture('Bruksmønster')
  .page(app.baseUrl)
  .beforeEach(async t => {
    await t
      .useRole(AutoTestUser)
      .maximizeWindow()
      .navigateTo(app.baseUrl + "designer/" + appName + "#/about");
  })

//Navigate around studio designer
test('Navigation', async() => {
  await t
    .click(designerPage.aboutNavigationTab)
    .expect(designerPage.appNameLocked.value).contains("auto", 'input contains text ' + appName);
  await t
    .click(designerPage.createNavigationTab)
    .expect(designerPage.inputComponent.exists).ok();
  await t
    .click(designerPage.languageNavigationTab)
    .switchToIframe('iframe')
    .expect(designerPage.languageTabs.visible).ok();
});

//Gitea connection
test('Gitea connection - Pull changes', async() => {
  await t
    .click(designerPage.pullChanges)
    .expect(Selector('h3').withText("Appen din er oppdatert til siste versjon").visible).ok();
});

//App builds and deploy information from cosmos
test('App builds and deploys', async() => {
  await t
    .click(designerPage.deployNavigationTab)
    .expect(getLocation()).contains('deploy')
    .expect(designerPage.appBuilds.child().count).gte(1);
  await t
    .expect(designerPage.deployTable.visible).ok()
    .expect(designerPage.deploys.count).gte(1);
});