@description('The name of the Azure Function App to deploy to.')
param functionAppName string

@description('The URL to the GitHub repository to deploy.')
param gitRepoUrl string = 'https://github.com/Microsoft/CallRecordInsights.git'

@description('The branch of the GitHub repository to deploy.')
param gitBranch string = 'main'

resource functionApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: functionAppName
}

resource sourceControl 'Microsoft.Web/sites/sourcecontrols@2022-09-01' = if (!empty(gitRepoUrl)) {
  name: 'web'
  parent: functionApp
  properties: {
    repoUrl: gitRepoUrl
    branch: gitBranch
    isManualIntegration: true
  }
}
