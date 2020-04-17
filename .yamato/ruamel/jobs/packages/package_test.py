from ruamel.yaml.scalarstring import DoubleQuotedScalarString as dss
from ..utils.namer import *
from ..utils.constants import PATH_TEST_RESULTS_padded, PATH_UNITY_REVISION

def get_job_definition(package, platform, editor):
    job = {
        'name': f'Test { package["name"] } {platform["name"]} {editor["version"]}',
        'agent': dict(platform["agent"]),
        'dependencies':[
            f'{editor_filepath()}#{editor_job_id(editor["version"], platform["os"]) }'
        ],
        'commands': [
            f'npm install upm-ci-utils@stable -g --registry https://api.bintray.com/npm/unity/unity-npm',
            f'pip install unity-downloader-cli --extra-index-url https://artifactory.internal.unity3d.com/api/pypi/common-python/simple --upgrade',
            f'unity-downloader-cli --source-file {PATH_UNITY_REVISION} -c editor --wait --published-only'
        ],
        'artifacts':{
            'logs':{
                'paths': [
                    dss(PATH_TEST_RESULTS_padded)
                ]
            }
        }
    }

    [job["dependencies"].append(f'{packages_filepath()}#{package_job_id_pack(dep)}') for dep in package["dependencies"]]

    if package.get('hascodependencies', None) is not None:
        job["commands"].append(platform["copycmd"])
    job["commands"].append(f'upm-ci package test -u {platform["editorpath"]} --package-path {package["packagename"]}')

    return job


class Package_TestJob():
    
    def __init__(self, package, platform, editor):
        self.package_id = package["id"]
        self.job_id = package_job_id_test(package["id"],platform["name"],editor["version"])
        self.yml = get_job_definition(package,platform, editor)


    
    
    